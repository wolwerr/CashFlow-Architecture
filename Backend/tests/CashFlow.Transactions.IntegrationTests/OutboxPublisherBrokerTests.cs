using System.Collections.Concurrent;
using System.Diagnostics;
using CashFlow.Transactions.Infrastructure.Entities;
using CashFlow.Transactions.Infrastructure.Messaging;
using CashFlow.Transactions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CashFlow.Transactions.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class OutboxPublisherBrokerTests
{
    [Fact]
    public async Task PublisherConfirmsRealRabbitMqDeliveryBeforeMarkingOutboxPublished()
    {
        if (Environment.GetEnvironmentVariable("CASHFLOW_TEST_RABBITMQ") != "true")
            return;

        var connection = Environment.GetEnvironmentVariable("CASHFLOW_TEST_POSTGRES")!;
        var options = new DbContextOptionsBuilder<TransactionsDbContext>().UseNpgsql(connection).Options;
        var id = Guid.NewGuid();
        await using (var setup = new TransactionsDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.OutboxMessages.Add(new OutboxMessageEntity
            {
                EventId = id, MerchantId = Guid.NewGuid(), TransactionId = Guid.NewGuid(),
                EventType = "transaction.recorded", SchemaVersion = "1",
                CorrelationId = "broker-integration", Payload = "{}",
                CreatedAtUtc = DateTimeOffset.UtcNow, AttemptCount = 0
            });
            await setup.SaveChangesAsync();
        }

        using var services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<TransactionsDbContext>(x => x.UseNpgsql(connection))
            .BuildServiceProvider();
        var rabbit = Options.Create(new RabbitMqConnectionSettings
        {
            Host = "localhost", Port = 5672, User = "cashflow",
            Password = "local_ci_only"
        });
        var spans = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "CashFlow.Transactions.Outbox",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => spans.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);

        using var worker = new OutboxPublisherWorker(
            services.GetRequiredService<IServiceScopeFactory>(), rabbit,
            NullLogger<OutboxPublisherWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        var confirmed = false;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await Task.Delay(150);
            await using var db = new TransactionsDbContext(options);
            confirmed = await db.OutboxMessages.AsNoTracking()
                .AnyAsync(x => x.EventId == id && x.PublishedAtUtc != null);
            if (confirmed) break;
        }
        await worker.StopAsync(CancellationToken.None);
        Assert.True(confirmed, "Outbox row must become published after RabbitMQ publisher confirm.");
        Assert.Contains(spans, span => span.DisplayName == "rabbitmq publish transaction.recorded"
            && span.Kind == ActivityKind.Producer
            && Equals(span.GetTagItem("messaging.system"), "rabbitmq"));
    }
}
