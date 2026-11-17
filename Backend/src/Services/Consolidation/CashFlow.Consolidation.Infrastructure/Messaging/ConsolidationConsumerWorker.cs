using System.Diagnostics;
using System.Text;
using CashFlow.Consolidation.Application.UseCases.ProcessTransaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CashFlow.Consolidation.Infrastructure.Messaging;

/// <summary>
/// Durable quorum queue, manual ACK after DB commit, requeue on transient failure,
/// poison messages go to DLQ, quorum delivery-limit bounds repeated requeues.
/// </summary>
public sealed class ConsolidationConsumerWorker(
    IServiceScopeFactory scopes, IOptions<RabbitMqConsumerSettings> config,
    ILogger<ConsolidationConsumerWorker> logger) : BackgroundService
{
    private static readonly ActivitySource Traces = new("CashFlow.Consolidation.Consumer");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = config.Value;
        settings.Validate();
        var factory = new ConnectionFactory
        {
            HostName = settings.Host, Port = settings.Port,
            UserName = settings.User, Password = settings.Password,
            AutomaticRecoveryEnabled = true
        };
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await factory.CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await channel.ExchangeDeclareAsync(settings.Exchange, ExchangeType.Direct,
                    durable: true, autoDelete: false, cancellationToken: stoppingToken);
                await channel.ExchangeDeclareAsync(settings.DeadLetterExchange, ExchangeType.Direct,
                    durable: true, autoDelete: false, cancellationToken: stoppingToken);
                await channel.QueueDeclareAsync(settings.DeadLetterQueue, durable: true,
                    exclusive: false, autoDelete: false,
                    arguments: new Dictionary<string, object?>
                    {
                        ["x-queue-type"] = "quorum"
                    }, cancellationToken: stoppingToken);
                await channel.QueueBindAsync(settings.DeadLetterQueue, settings.DeadLetterExchange,
                    settings.Queue, cancellationToken: stoppingToken);
                await channel.QueueDeclareAsync(settings.Queue, durable: true,
                    exclusive: false, autoDelete: false,
                    arguments: new Dictionary<string, object?>
                    {
                        ["x-queue-type"] = "quorum",
                        ["x-dead-letter-exchange"] = settings.DeadLetterExchange,
                        ["x-dead-letter-routing-key"] = settings.Queue,
                        ["x-delivery-limit"] = 5
                    }, cancellationToken: stoppingToken);
                await channel.QueueBindAsync(settings.Queue, settings.Exchange,
                    "transaction.recorded", cancellationToken: stoppingToken);
                await channel.BasicQosAsync(0, 1, global: false, cancellationToken: stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, delivery) =>
                {
                    string? parentId = null;
                    if (delivery.BasicProperties.Headers?.TryGetValue("traceparent", out var header) == true)
                    {
                        parentId = header switch
                        {
                            byte[] bytes => Encoding.UTF8.GetString(bytes),
                            ReadOnlyMemory<byte> memory => Encoding.UTF8.GetString(memory.Span),
                            string value => value,
                            _ => null
                        };
                    }
                    // RabbitMQ delivery headers are untrusted. Invalid traceparent values
                    // simply create a new root span rather than changing authorization.
                    if (parentId is not null && !ActivityContext.TryParse(parentId, null, out _))
                        parentId = null;
                    using var activity = Traces.StartActivity(
                        "rabbitmq consume transaction.recorded", ActivityKind.Consumer, parentId);
                    activity?.SetTag("messaging.system", "rabbitmq");
                    activity?.SetTag("messaging.destination.name", settings.Queue);
                    var body = delivery.Body.ToArray();
                    TransactionRecorded message;
                    try
                    {
                        message = TransactionRecordedEventParser.Parse(body);
                    }
                    catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
                    {
                        logger.LogWarning(ex, "Poison transaction event sent to DLQ");
                        await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false,
                            cancellationToken: stoppingToken);
                        return;
                    }

                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        var handler = scope.ServiceProvider.GetRequiredService<ProcessTransactionUseCase>();
                        await handler.ExecuteAsync(message, stoppingToken);
                        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false,
                            cancellationToken: stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        // Connection close requeues any unacknowledged delivery.
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Projection failed for event {EventId}, requeueing", message.EventId);
                        await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
                        await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false,
                            requeue: true, cancellationToken: stoppingToken);
                    }
                };
                await channel.BasicConsumeAsync(settings.Queue, autoAck: false,
                    consumer: consumer, cancellationToken: stoppingToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Consumer disconnected; RabbitMQ will retain unacknowledged events");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
