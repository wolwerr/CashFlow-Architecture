using CashFlow.Consolidation.Application.UseCases.ProcessTransaction;
using CashFlow.Consolidation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CashFlow.Consolidation.IntegrationTests;

[Collection("Postgres consolidation")]
public sealed class ConsolidationPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);

    private static DbContextOptions<ConsolidationDbContext> Options()
    {
        var cs = Environment.GetEnvironmentVariable("CASHFLOW_TEST_CONSOLIDATION");
        Assert.False(string.IsNullOrWhiteSpace(cs), "A dedicated PostgreSQL connection is required.");
        return new DbContextOptionsBuilder<ConsolidationDbContext>().UseNpgsql(cs).Options;
    }

    private static TransactionRecorded Event(Guid merchant, DateOnly day, string type,
        decimal amount, Guid? eventId = null) =>
        new(eventId ?? Guid.NewGuid(), Guid.NewGuid(), merchant, type, amount, day, Now);

    [Fact]
    public async Task MigrationCreatesBothTablesAndIsIdempotent()
    {
        await using var db = new ConsolidationDbContext(Options());
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        Assert.Contains("202611160002_InitialConsolidation", await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task DuplicateEventDoesNotDoubleCountAndOtherMerchantRemainsIsolated()
    {
        var options = Options();
        await using var firstDb = new ConsolidationDbContext(options);
        await firstDb.Database.MigrateAsync();
        var merchant = Guid.NewGuid();
        var date = new DateOnly(2026, 11, 14);
        var message = Event(merchant, date, "CREDIT", 120.15m);
        Assert.True(await new ConsolidationGateway(firstDb).ApplyAsync(message, default));
        await using var retryDb = new ConsolidationDbContext(options);
        Assert.False(await new ConsolidationGateway(retryDb).ApplyAsync(message, default));

        await using var verifyDb = new ConsolidationDbContext(options);
        var gateway = new ConsolidationGateway(verifyDb);
        var result = await gateway.GetDailyAsync(merchant, date, default);
        Assert.Equal(120.15m, result.Credit);
        Assert.Equal(0m, result.Debit);
        Assert.Equal(120.15m, result.Closing);
        Assert.Equal(0m, (await gateway.GetDailyAsync(Guid.NewGuid(), date, default)).Closing);
        Assert.Equal(1, await verifyDb.ProcessedEvents.CountAsync(x => x.EventId == message.EventId));
    }

    [Fact]
    public async Task EmptyDayCarriesBalanceAndRetroactiveEntriesRecomputeClosing()
    {
        var options = Options();
        await using var db = new ConsolidationDbContext(options);
        await db.Database.MigrateAsync();
        var merchant = Guid.NewGuid();
        var gateway = new ConsolidationGateway(db);
        var d1 = new DateOnly(2026, 11, 1);
        var d2 = new DateOnly(2026, 11, 3);
        await gateway.ApplyAsync(Event(merchant, d2, "DEBIT", 20.10m), default);
        Assert.Equal(-20.10m, (await gateway.GetDailyAsync(merchant, d2, default)).Closing);
        await gateway.ApplyAsync(Event(merchant, d1, "CREDIT", 80.30m), default);
        Assert.Equal(80.30m, (await gateway.GetDailyAsync(merchant, d1, default)).Closing);
        var empty = await gateway.GetDailyAsync(merchant, new DateOnly(2026, 11, 2), default);
        Assert.Equal(0m, empty.Credit);
        Assert.Equal(0m, empty.Debit);
        Assert.Equal(80.30m, empty.Closing);
        Assert.Equal(60.20m, (await gateway.GetDailyAsync(merchant, d2, default)).Closing);
    }

    [Fact]
    public async Task ConcurrentEventsAccumulateWithoutLostUpdates()
    {
        var options = Options();
        await using (var db = new ConsolidationDbContext(options)) await db.Database.MigrateAsync();
        var merchant = Guid.NewGuid();
        var day = new DateOnly(2026, 10, 20);
        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = new ConsolidationDbContext(options);
            return await new ConsolidationGateway(db)
                .ApplyAsync(Event(merchant, day, "CREDIT", 1.25m), default);
        });
        Assert.All(await Task.WhenAll(tasks), Assert.True);
        await using var verify = new ConsolidationDbContext(options);
        Assert.Equal(10.00m,
            (await new ConsolidationGateway(verify).GetDailyAsync(merchant, day, default)).Closing);
    }

    [Fact]
    public async Task InvalidAmountCausesPostgresRollbackOfProcessedMarker()
    {
        var options = Options();
        await using var db = new ConsolidationDbContext(options);
        await db.Database.MigrateAsync();
        var merchant = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var message = Event(merchant, new DateOnly(2026, 11, 15), "CREDIT",
            10000000000000000000000000m, eventId);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            new ConsolidationGateway(db).ApplyAsync(message, default));
        await using var verify = new ConsolidationDbContext(options);
        Assert.False(await verify.ProcessedEvents.AnyAsync(x => x.EventId == eventId));
    }
}
