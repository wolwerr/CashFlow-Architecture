using CashFlow.Transactions.Infrastructure.Entities;
using CashFlow.Transactions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CashFlow.Transactions.UnitTests;

public sealed class TransactionsPersistenceModelTests
{
    private static TransactionsDbContext CreateContext() => new(
        new DbContextOptionsBuilder<TransactionsDbContext>()
            .UseNpgsql("Host=localhost;Database=cashflow_schema_test;Username=test;Password=test")
            .Options);

    [Fact]
    public void ModelContainsFourPersistenceTablesWithoutConnectingToDatabase()
    {
        using var context = CreateContext();
        var tableNames = context.Model.GetEntityTypes().Select(e => e.GetTableName()).ToArray();
        Assert.Equal(4, tableNames.Length);
        Assert.Contains("transactions", tableNames);
        Assert.Contains("outbox_messages", tableNames);
        Assert.Contains("transaction_audits", tableNames);
        Assert.Contains("idempotency_records", tableNames);
    }

    [Fact]
    public void TransactionMoneyAndDateUseExplicitPostgresTypes()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(TransactionEntity))!;
        Assert.Equal("numeric(19,2)", entity.FindProperty(nameof(TransactionEntity.Amount))!.GetColumnType());
        Assert.Equal("date", entity.FindProperty(nameof(TransactionEntity.ReferenceDate))!.GetColumnType());
        Assert.Equal("smallint", entity.FindProperty(nameof(TransactionEntity.Type))!.GetColumnType());
        Assert.Equal("timestamp with time zone", entity.FindProperty(nameof(TransactionEntity.CreatedAtUtc))!.GetColumnType());
        Assert.Contains(entity.GetCheckConstraints(), c => c.Name == "ck_transactions_amount");
        Assert.Contains(entity.GetCheckConstraints(), c => c.Name == "ck_transactions_type");
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(
            [nameof(TransactionEntity.MerchantId), nameof(TransactionEntity.ReferenceDate), nameof(TransactionEntity.Id)]));
    }

    [Fact]
    public void IdempotencyIsUniquePerMerchantAndRequestKey()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(IdempotencyRecordEntity))!;
        Assert.Equal([nameof(IdempotencyRecordEntity.MerchantId), nameof(IdempotencyRecordEntity.RequestKey)],
            entity.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());
        Assert.Equal(128, entity.FindProperty(nameof(IdempotencyRecordEntity.RequestKey))!.GetMaxLength());
        Assert.Equal(64, entity.FindProperty(nameof(IdempotencyRecordEntity.RequestHash))!.GetMaxLength());
    }

    [Fact]
    public void OutboxPreservesVersionedJsonPayloadAndPendingIndex()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(OutboxMessageEntity))!;
        Assert.Equal("jsonb", entity.FindProperty(nameof(OutboxMessageEntity.Payload))!.GetColumnType());
        Assert.Equal(128, entity.FindProperty(nameof(OutboxMessageEntity.CorrelationId))!.GetMaxLength());
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(
            [nameof(OutboxMessageEntity.PublishedAtUtc), nameof(OutboxMessageEntity.CreatedAtUtc)]));
        Assert.Contains(entity.GetCheckConstraints(), c => c.Name == "ck_outbox_attempts");
    }

    [Fact]
    public void AuditIsScopedByMerchantAndTimestamp()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(TransactionAuditEntity))!;
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(
            [nameof(TransactionAuditEntity.MerchantId), nameof(TransactionAuditEntity.OccurredAtUtc)]));
        Assert.Equal(128, entity.FindProperty(nameof(TransactionAuditEntity.CorrelationId))!.GetMaxLength());
    }
}
