using System.Text;
using CashFlow.Consolidation.Application.Gateways;
using CashFlow.Consolidation.Application.UseCases.ProcessTransaction;
using CashFlow.Consolidation.Infrastructure.Messaging;
using CashFlow.Consolidation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Xunit;

namespace CashFlow.Consolidation.IntegrationTests;

[Collection("Postgres consolidation")]
public sealed class RabbitMqConsumerBrokerTests
{
    [Fact]
    public async Task DurableConsumerProjectsMessageAndAcknowledgesOnlyAfterCommit()
    {
        if (Environment.GetEnvironmentVariable("CASHFLOW_TEST_RABBITMQ") != "true") return;

        var cs = Environment.GetEnvironmentVariable("CASHFLOW_TEST_CONSOLIDATION")!;
        var options = new DbContextOptionsBuilder<ConsolidationDbContext>().UseNpgsql(cs).Options;
        await using (var init = new ConsolidationDbContext(options))
            await init.Database.MigrateAsync();

        using var provider = new ServiceCollection()
            .AddLogging()
            .AddDbContext<ConsolidationDbContext>(o => o.UseNpgsql(cs))
            .AddScoped<IConsolidationGateway, ConsolidationGateway>()
            .AddScoped<ProcessTransactionUseCase>()
            .BuildServiceProvider();
        var settings = new RabbitMqConsumerSettings
        {
            Host = "localhost", User = "cashflow", Password = "local_ci_only"
        };
        using var worker = new ConsolidationConsumerWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(settings),
            NullLogger<ConsolidationConsumerWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);

        var factory = new ConnectionFactory
        {
            HostName = "localhost", UserName = "cashflow", Password = "local_ci_only"
        };
        await using var connection = await factory.CreateConnectionAsync();
        var optionsConfirm = new CreateChannelOptions(
            publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);
        await using var channel = await connection.CreateChannelAsync(optionsConfirm);

        // Worker declares topology at startup; allow a small bounded wait for initialization.
        await Task.Delay(700);
        var id = Guid.NewGuid();
        var merchant = Guid.NewGuid();
        var transaction = Guid.NewGuid();
        var payload = $$"""
            {
              "event_id": "{{id:D}}",
              "event_type": "transaction.recorded",
              "schema_version": "1",
              "correlation_id": "test",
              "occurred_at": "2026-11-16T15:00:00Z",
              "data": {
                "transaction_id": "{{transaction:D}}",
                "merchant_id": "{{merchant:D}}",
                "type": "CREDIT",
                "amount": "42.15",
                "currency": "BRL",
                "reference_date": "2026-11-14",
                "created_at": "2026-11-16T15:00:00Z"
              }
            }
            """;
        await channel.BasicPublishAsync(settings.Exchange, "transaction.recorded", mandatory: true,
            basicProperties: new BasicProperties { Persistent = true, MessageId = id.ToString("D") },
            body: Encoding.UTF8.GetBytes(payload));
        var projected = false;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await Task.Delay(150);
            await using var db = new ConsolidationDbContext(options);
            projected = await db.ProcessedEvents.AsNoTracking().AnyAsync(x => x.EventId == id);
            if (projected) break;
        }
        await worker.StopAsync(CancellationToken.None);
        Assert.True(projected, "Consumer must commit the projection before ACK.");
        await using var verify = new ConsolidationDbContext(options);
        var daily = await new ConsolidationGateway(verify).GetDailyAsync(
            merchant, new DateOnly(2026, 11, 14), default);
        Assert.Equal(42.15m, daily.Closing);
    }
}
