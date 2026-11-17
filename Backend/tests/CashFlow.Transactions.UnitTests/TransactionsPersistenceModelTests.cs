using CashFlow.Transactions.Infrastructure.Entities;
using CashFlow.Transactions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
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
        Assert.Contains(context.GetService<IDesignTimeModel>().Model.FindEntityType(entity.ClrType)!.GetCheckConstraints(), c => c.Name == "ck_transactions_amount");
        Assert.Contains(context.GetService<IDesignTimeModel>().Model.FindEntityType(entity.ClrType)!.GetCheckConstraints(), c => c.Name == "ck_transactions_type");
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
        Assert.Equal(55, entity.FindProperty(nameof(OutboxMessageEntity.TraceParent))!.GetMaxLength());
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(
            [nameof(OutboxMessageEntity.PublishedAtUtc), nameof(OutboxMessageEntity.CreatedAtUtc)]));
        Assert.Contains(context.GetService<IDesignTimeModel>().Model.FindEntityType(entity.ClrType)!.GetCheckConstraints(), c => c.Name == "ck_outbox_attempts");
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
    [Fact]
    public void PersistenceEntitiesPreserveAssignedFields()
    {
        var merchantId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);
        var date = new DateOnly(2026, 11, 16);

        var transaction = new TransactionEntity
        {
            Id = transactionId,
            MerchantId = merchantId,
            ActorId = actorId,
            Type = CashFlow.Transactions.Domain.Enums.TransactionType.Debit,
            Amount = 99.55m,
            ReferenceDate = date,
            CreatedAtUtc = timestamp
        };
        Assert.Equal(transactionId, transaction.Id);
        Assert.Equal(merchantId, transaction.MerchantId);
        Assert.Equal(actorId, transaction.ActorId);
        Assert.Equal(CashFlow.Transactions.Domain.Enums.TransactionType.Debit, transaction.Type);
        Assert.Equal(99.55m, transaction.Amount);
        Assert.Equal(date, transaction.ReferenceDate);
        Assert.Equal(timestamp, transaction.CreatedAtUtc);

        var outbox = new OutboxMessageEntity
        {
            EventId = eventId,
            MerchantId = merchantId,
            TransactionId = transactionId,
            EventType = "transaction.recorded",
            SchemaVersion = "1",
            CorrelationId = "correlation",
            Payload = "{\"event_type\":\"transaction.recorded\"}",
            CreatedAtUtc = timestamp,
            PublishedAtUtc = timestamp.AddSeconds(3),
            AttemptCount = 2
        };
        Assert.Equal(eventId, outbox.EventId);
        Assert.Equal(merchantId, outbox.MerchantId);
        Assert.Equal(transactionId, outbox.TransactionId);
        Assert.Equal("transaction.recorded", outbox.EventType);
        Assert.Equal("1", outbox.SchemaVersion);
        Assert.Equal("correlation", outbox.CorrelationId);
        Assert.Contains("event_type", outbox.Payload);
        Assert.Equal(timestamp, outbox.CreatedAtUtc);
        Assert.Equal(timestamp.AddSeconds(3), outbox.PublishedAtUtc);
        Assert.Equal(2, outbox.AttemptCount);

        var auditId = Guid.NewGuid();
        var audit = new TransactionAuditEntity
        {
            Id = auditId,
            TransactionId = transactionId,
            MerchantId = merchantId,
            ActorId = actorId,
            Action = "CREATED",
            CorrelationId = "correlation",
            OccurredAtUtc = timestamp
        };
        Assert.Equal(auditId, audit.Id);
        Assert.Equal(transactionId, audit.TransactionId);
        Assert.Equal(merchantId, audit.MerchantId);
        Assert.Equal(actorId, audit.ActorId);
        Assert.Equal("CREATED", audit.Action);
        Assert.Equal("correlation", audit.CorrelationId);
        Assert.Equal(timestamp, audit.OccurredAtUtc);

        var idempotency = new IdempotencyRecordEntity
        {
            MerchantId = merchantId,
            RequestKey = "request-key",
            RequestHash = new string('a', 64),
            TransactionId = transactionId,
            CreatedAtUtc = timestamp
        };
        Assert.Equal(merchantId, idempotency.MerchantId);
        Assert.Equal("request-key", idempotency.RequestKey);
        Assert.Equal(64, idempotency.RequestHash.Length);
        Assert.Equal(transactionId, idempotency.TransactionId);
        Assert.Equal(timestamp, idempotency.CreatedAtUtc);

        using var context = CreateContext();
        Assert.NotNull(context.Transactions);
        Assert.NotNull(context.OutboxMessages);
        Assert.NotNull(context.TransactionAudits);
        Assert.NotNull(context.IdempotencyRecords);
    }

}
