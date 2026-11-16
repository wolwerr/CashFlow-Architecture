using System.Text.Json;
using CashFlow.Transactions.Application.Exceptions;
using CashFlow.Transactions.Domain.Enums;
using CashFlow.Transactions.Domain.Models;
using CashFlow.Transactions.Infrastructure.Gateways;
using CashFlow.Transactions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CashFlow.Transactions.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class TransactionRegistrationGatewayTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);

    private static DbContextOptions<TransactionsDbContext> Options()
    {
        var connection = Environment.GetEnvironmentVariable("CASHFLOW_TEST_POSTGRES");
        Assert.False(string.IsNullOrWhiteSpace(connection),
            "Use a dedicated disposable PostgreSQL database in CASHFLOW_TEST_POSTGRES.");
        return new DbContextOptionsBuilder<TransactionsDbContext>().UseNpgsql(connection).Options;
    }

    private static Transaction NewTransaction(
        Guid merchant, Guid actor, decimal amount = 10.25m, TransactionType type = TransactionType.Credit) =>
        Transaction.Create(merchant, actor, type, amount, new DateOnly(2026, 11, 16),
            new DateOnly(2026, 11, 16), Now);

    [Fact]
    public async Task RegistrationCommitsFourRowsAndValidVersionedOutboxPayload()
    {
        await using var db = new TransactionsDbContext(Options());
        await db.Database.MigrateAsync();
        var merchant = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var transaction = NewTransaction(merchant, actor);
        var gateway = new TransactionRegistrationGateway(db);
        var result = await gateway.RegisterAsync(transaction, "key-" + Guid.NewGuid(), "trace-1", default);

        Assert.Equal(transaction.Id, result.Id);
        Assert.Equal(1, await db.Transactions.AsNoTracking().CountAsync(x => x.Id == result.Id));
        Assert.Equal(1, await db.OutboxMessages.AsNoTracking().CountAsync(x => x.TransactionId == result.Id));
        Assert.Equal(1, await db.TransactionAudits.AsNoTracking().CountAsync(x => x.TransactionId == result.Id));
        Assert.Equal(1, await db.IdempotencyRecords.AsNoTracking().CountAsync(x => x.TransactionId == result.Id));

        var outbox = await db.OutboxMessages.AsNoTracking().SingleAsync(x => x.TransactionId == result.Id);
        Assert.Equal("transaction.recorded", outbox.EventType);
        Assert.Equal("1", outbox.SchemaVersion);
        Assert.Null(outbox.PublishedAtUtc);
        using var payload = JsonDocument.Parse(outbox.Payload);
        Assert.Equal(outbox.EventId.ToString("D"), payload.RootElement.GetProperty("event_id").GetString());
        Assert.Equal("transaction.recorded", payload.RootElement.GetProperty("event_type").GetString());
        Assert.Equal("1", payload.RootElement.GetProperty("schema_version").GetString());
        Assert.Equal("BRL", payload.RootElement.GetProperty("data").GetProperty("currency").GetString());
        Assert.Equal("10.25", payload.RootElement.GetProperty("data").GetProperty("amount").GetString());
        Assert.Equal("CREDIT", payload.RootElement.GetProperty("data").GetProperty("type").GetString());
        Assert.Equal("2026-11-16", payload.RootElement.GetProperty("data").GetProperty("reference_date").GetString());
    }

    [Fact]
    public async Task SameMerchantAndKeyReplaysCommittedResultAndDifferentPayloadConflicts()
    {
        var options = Options();
        await using var firstDb = new TransactionsDbContext(options);
        await firstDb.Database.MigrateAsync();

        var merchant = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var key = "key-" + Guid.NewGuid();
        var first = await new TransactionRegistrationGateway(firstDb)
            .RegisterAsync(NewTransaction(merchant, actor), key, "original", default);

        await using var retryDb = new TransactionsDbContext(options);
        var replayed = await new TransactionRegistrationGateway(retryDb)
            .RegisterAsync(NewTransaction(merchant, actor), key, "retry-different-correlation", default);
        Assert.Equal(first, replayed);

        await using var conflictDb = new TransactionsDbContext(options);
        await Assert.ThrowsAsync<TransactionIdempotencyConflictException>(() =>
            new TransactionRegistrationGateway(conflictDb)
                .RegisterAsync(NewTransaction(merchant, actor, 17m), key, "conflict", default));

        await using var verification = new TransactionsDbContext(options);
        Assert.Equal(1, await verification.IdempotencyRecords.AsNoTracking()
            .CountAsync(x => x.MerchantId == merchant && x.RequestKey == key));
        Assert.Equal(1, await verification.Transactions.AsNoTracking()
            .CountAsync(x => x.MerchantId == merchant));
        Assert.Equal(1, await verification.OutboxMessages.AsNoTracking()
            .CountAsync(x => x.MerchantId == merchant));
        Assert.Equal(1, await verification.TransactionAudits.AsNoTracking()
            .CountAsync(x => x.MerchantId == merchant));
    }

    [Fact]
    public async Task ConcurrentDuplicateRequestsCommitOnlyOneTransactionAndOneEvent()
    {
        var options = Options();
        await using (var init = new TransactionsDbContext(options))
            await init.Database.MigrateAsync();

        var merchant = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var key = "key-" + Guid.NewGuid();

        async Task<Guid> Submit()
        {
            await using var db = new TransactionsDbContext(options);
            var result = await new TransactionRegistrationGateway(db)
                .RegisterAsync(NewTransaction(merchant, actor), key, "parallel", default);
            return result.Id;
        }

        var responses = await Task.WhenAll(Submit(), Submit());
        Assert.Equal(responses[0], responses[1]);

        await using var check = new TransactionsDbContext(options);
        Assert.Equal(1, await check.Transactions.AsNoTracking().CountAsync(x => x.MerchantId == merchant));
        Assert.Equal(1, await check.OutboxMessages.AsNoTracking().CountAsync(x => x.MerchantId == merchant));
        Assert.Equal(1, await check.TransactionAudits.AsNoTracking().CountAsync(x => x.MerchantId == merchant));
        Assert.Equal(1, await check.IdempotencyRecords.AsNoTracking().CountAsync(x => x.MerchantId == merchant));
    }

    [Fact]
    public async Task FailureInOutboxInsertionRollsBackRegistrationAndAllowsSameKeyToRetry()
    {
        var options = Options();
        await using var db = new TransactionsDbContext(options);
        await db.Database.MigrateAsync();
        var merchant = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var key = "key-" + Guid.NewGuid();
        var transaction = NewTransaction(merchant, actor);

        // Direct gateway call with oversized correlation forces PostgreSQL length violation
        // after idempotency reservation. The transaction must roll back every write.
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            new TransactionRegistrationGateway(db).RegisterAsync(
                transaction, key, new string('x', 129), default));

        await using var verification = new TransactionsDbContext(options);
        Assert.Empty(await verification.IdempotencyRecords.AsNoTracking()
            .Where(x => x.MerchantId == merchant && x.RequestKey == key).ToListAsync());
        Assert.Empty(await verification.Transactions.AsNoTracking()
            .Where(x => x.Id == transaction.Id).ToListAsync());
        Assert.Empty(await verification.OutboxMessages.AsNoTracking()
            .Where(x => x.TransactionId == transaction.Id).ToListAsync());
        Assert.Empty(await verification.TransactionAudits.AsNoTracking()
            .Where(x => x.TransactionId == transaction.Id).ToListAsync());

        var result = await new TransactionRegistrationGateway(verification).RegisterAsync(
            NewTransaction(merchant, actor), key, "good-retry", default);
        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task SameKeyForDifferentMerchantDoesNotConflict()
    {
        var options = Options();
        await using var init = new TransactionsDbContext(options);
        await init.Database.MigrateAsync();

        var key = "key-" + Guid.NewGuid();
        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var first = await new TransactionRegistrationGateway(init)
            .RegisterAsync(NewTransaction(merchantA, actor), key, "merchant-a", default);

        await using var secondDb = new TransactionsDbContext(options);
        var second = await new TransactionRegistrationGateway(secondDb)
            .RegisterAsync(NewTransaction(merchantB, actor), key, "merchant-b", default);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, await secondDb.IdempotencyRecords.AsNoTracking().CountAsync(x => x.RequestKey == key));
    }
}
