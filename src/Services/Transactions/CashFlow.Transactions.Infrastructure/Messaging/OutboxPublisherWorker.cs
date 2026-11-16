using System.Text;
using CashFlow.Transactions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace CashFlow.Transactions.Infrastructure.Messaging;

/// <summary>
/// Publishes only committed Outbox records. PostgreSQL row locks serialize multiple workers.
/// Holding the row lock across broker confirmation deliberately favors correctness over throughput.
/// If confirmation succeeds but DB commit fails, redelivery is safe due to consumer event_id uniqueness.
/// </summary>
public sealed class OutboxPublisherWorker(
    IServiceScopeFactory scopes, IOptions<RabbitMqConnectionSettings> config,
    ILogger<OutboxPublisherWorker> logger) : BackgroundService
{
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
                var confirmations = new CreateChannelOptions(
                    publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);
                await using var channel = await connection.CreateChannelAsync(confirmations, stoppingToken);
                await channel.ExchangeDeclareAsync(settings.Exchange, ExchangeType.Direct,
                    durable: true, autoDelete: false, cancellationToken: stoppingToken);
                // Declare/bind destination BEFORE publishing, to avoid unroutable messages.
                await channel.QueueDeclareAsync(settings.Queue, durable: true, exclusive: false,
                    autoDelete: false, arguments: null, cancellationToken: stoppingToken);
                await channel.QueueBindAsync(settings.Queue, settings.Exchange,
                    "transaction.recorded", cancellationToken: stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    if (!await PublishOneAsync(channel, settings, stoppingToken))
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox publisher disconnected; pending events remain durable");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<bool> PublishOneAsync(
        IChannel channel, RabbitMqConnectionSettings settings, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var row = await db.OutboxMessages.FromSqlRaw("""
            SELECT * FROM outbox_messages
            WHERE published_at_utc IS NULL
            ORDER BY created_at_utc, event_id
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """).FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            await tx.CommitAsync(cancellationToken);
            return false;
        }

        // Use publisher confirmation tracking plus mandatory routing. Ack or return is awaited.
        try
        {
            await channel.BasicPublishAsync(settings.Exchange, "transaction.recorded",
                mandatory: true,
                basicProperties: new BasicProperties
                {
                    Persistent = true, ContentType = "application/json",
                    MessageId = row.EventId.ToString("D"), Type = row.EventType,
                    CorrelationId = row.CorrelationId
                },
                body: Encoding.UTF8.GetBytes(row.Payload), cancellationToken: cancellationToken);

            row.PublishedAtUtc = DateTimeOffset.UtcNow;
            row.AttemptCount++;
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            row.AttemptCount++;
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            logger.LogWarning(ex, "Event {EventId} not confirmed; retained in Outbox", row.EventId);
            return false;
        }
    }
}
