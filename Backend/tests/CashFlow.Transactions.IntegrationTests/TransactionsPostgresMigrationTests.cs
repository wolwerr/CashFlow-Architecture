using CashFlow.Transactions.Domain.Enums;
using CashFlow.Transactions.Infrastructure.Entities;
using CashFlow.Transactions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CashFlow.Transactions.IntegrationTests;

/// <summary>Executes against a dedicated LOCAL PostgreSQL database, never production.</summary>
[Collection("PostgreSQL integration")]
public sealed class TransactionsPostgresMigrationTests
{
    [Fact]
    public async Task MigrationCreatesTablesAndConstraintsAndPreservesRollback()
    {
        var connection = Environment.GetEnvironmentVariable("CASHFLOW_TEST_POSTGRES");
        Assert.False(string.IsNullOrWhiteSpace(connection),
            "Set CASHFLOW_TEST_POSTGRES to a disposable LOCAL PostgreSQL database before running integration tests.");

        var options = new DbContextOptionsBuilder<TransactionsDbContext>()
            .UseNpgsql(connection).Options;

        await using var db = new TransactionsDbContext(options);

        // Execute migrations twice to verify that already-applied migrations are not reapplied.
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains("202611160001_InitialTransactionsPersistence", applied);
        Assert.Contains("202611170001_AddOutboxTraceParent", applied);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        var merchant = Guid.NewGuid();
        var otherMerchant = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var id = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);

        db.Transactions.Add(new TransactionEntity
        {
            Id = id, MerchantId = merchant, ActorId = actor,
            Type = TransactionType.Credit, Amount = 120.25m,
            ReferenceDate = new DateOnly(2026, 11, 16), CreatedAtUtc = now
        });
        db.OutboxMessages.Add(new OutboxMessageEntity
        {
            EventId = Guid.NewGuid(), MerchantId = merchant,
            TransactionId = id, EventType = "transaction.recorded",
            SchemaVersion = "1", CorrelationId = "integration-test",
            Payload = "{\"event_id\":\"test\"}", CreatedAtUtc = now
        });
        db.TransactionAudits.Add(new TransactionAuditEntity
        {
            Id = Guid.NewGuid(), TransactionId = id,
            MerchantId = merchant, ActorId = actor, Action = "CREATED",
            CorrelationId = "integration-test", OccurredAtUtc = now
        });
        db.IdempotencyRecords.Add(new IdempotencyRecordEntity
        {
            MerchantId = merchant, RequestKey = "integration-key",
            RequestHash = new string('a', 64), TransactionId = id, CreatedAtUtc = now
        });
        Assert.Equal(4, await db.SaveChangesAsync());

        Assert.Single(await db.Transactions.AsNoTracking().Where(x => x.MerchantId == merchant).ToListAsync());
        Assert.Empty(await db.Transactions.AsNoTracking().Where(x => x.MerchantId == otherMerchant).ToListAsync());
        Assert.Single(await db.OutboxMessages.AsNoTracking().Where(x => x.TransactionId == id).ToListAsync());
        Assert.Single(await db.TransactionAudits.AsNoTracking().Where(x => x.TransactionId == id).ToListAsync());
        Assert.Single(await db.IdempotencyRecords.AsNoTracking().Where(x => x.TransactionId == id).ToListAsync());

        // Unique merchant/key constraint is enforced by PostgreSQL, not an in-memory mock.
        db.ChangeTracker.Clear();
        db.IdempotencyRecords.Add(new IdempotencyRecordEntity
        {
            MerchantId = merchant, RequestKey = "integration-key",
            RequestHash = new string('b', 64), TransactionId = Guid.NewGuid(), CreatedAtUtc = now
        });
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
        db.ChangeTracker.Clear();

        // Actual DB CHECK constraint: financial amount must be positive.
        db.Transactions.Add(new TransactionEntity
        {
            Id = Guid.NewGuid(), MerchantId = merchant, ActorId = actor,
            Type = TransactionType.Debit, Amount = 0m,
            ReferenceDate = new DateOnly(2026, 11, 16), CreatedAtUtc = now
        });
        var check = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(check.InnerException).SqlState);
        db.ChangeTracker.Clear();

        // A transaction rolled back must leave no financial record in PostgreSQL.
        var rolledBackId = Guid.NewGuid();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.Transactions.Add(new TransactionEntity
            {
                Id = rolledBackId, MerchantId = merchant, ActorId = actor,
                Type = TransactionType.Debit, Amount = 12m,
                ReferenceDate = new DateOnly(2026, 11, 16), CreatedAtUtc = now
            });
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        db.ChangeTracker.Clear();
        Assert.False(await db.Transactions.AsNoTracking().AnyAsync(x => x.Id == rolledBackId));
    }
}
