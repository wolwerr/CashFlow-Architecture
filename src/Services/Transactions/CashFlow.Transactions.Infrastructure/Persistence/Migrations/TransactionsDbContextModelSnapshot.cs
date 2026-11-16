using System.Diagnostics.CodeAnalysis;
using CashFlow.Transactions.Domain.Enums;
using CashFlow.Transactions.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CashFlow.Transactions.Infrastructure.Persistence.Migrations;

/// <summary>Immutable baseline model snapshot for future EF Core migrations.</summary>
[ExcludeFromCodeCoverage]
[DbContext(typeof(TransactionsDbContext))]
public sealed class TransactionsDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.3");

        modelBuilder.Entity<TransactionEntity>(entity =>
        {
            entity.ToTable("transactions", table =>
            {
                table.HasCheckConstraint("ck_transactions_amount", "amount > 0");
                table.HasCheckConstraint("ck_transactions_type", "type IN (1, 2)");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(x => x.MerchantId).HasColumnName("merchant_id").IsRequired();
            entity.Property(x => x.ActorId).HasColumnName("actor_id").IsRequired();
            entity.Property(x => x.Type).HasColumnName("type").HasConversion<short>().HasColumnType("smallint").IsRequired();
            entity.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(19,2)").IsRequired();
            entity.Property(x => x.ReferenceDate).HasColumnName("reference_date").HasColumnType("date").IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone").IsRequired();
            entity.HasIndex(x => new { x.MerchantId, x.ReferenceDate, x.Id });
        });

        modelBuilder.Entity<OutboxMessageEntity>(entity =>
        {
            entity.ToTable("outbox_messages", table => table.HasCheckConstraint("ck_outbox_attempts", "attempt_count >= 0"));
            entity.HasKey(x => x.EventId);
            entity.Property(x => x.EventId).HasColumnName("event_id").ValueGeneratedNever();
            entity.Property(x => x.MerchantId).HasColumnName("merchant_id").IsRequired();
            entity.Property(x => x.TransactionId).HasColumnName("transaction_id").IsRequired();
            entity.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(128).IsRequired();
            entity.Property(x => x.SchemaVersion).HasColumnName("schema_version").HasMaxLength(16).IsRequired();
            entity.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
            entity.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc").HasColumnType("timestamp with time zone");
            entity.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0);
            entity.HasIndex(x => new { x.PublishedAtUtc, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.MerchantId, x.TransactionId });
        });

        modelBuilder.Entity<TransactionAuditEntity>(entity =>
        {
            entity.ToTable("transaction_audits");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(x => x.TransactionId).HasColumnName("transaction_id");
            entity.Property(x => x.MerchantId).HasColumnName("merchant_id");
            entity.Property(x => x.ActorId).HasColumnName("actor_id");
            entity.Property(x => x.Action).HasColumnName("action").HasMaxLength(32).IsRequired();
            entity.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").HasColumnType("timestamp with time zone");
            entity.HasIndex(x => new { x.MerchantId, x.OccurredAtUtc });
            entity.HasIndex(x => x.TransactionId);
        });

        modelBuilder.Entity<IdempotencyRecordEntity>(entity =>
        {
            entity.ToTable("idempotency_records");
            entity.HasKey(x => new { x.MerchantId, x.RequestKey });
            entity.Property(x => x.MerchantId).HasColumnName("merchant_id");
            entity.Property(x => x.RequestKey).HasColumnName("request_key").HasMaxLength(128);
            entity.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(64).IsRequired();
            entity.Property(x => x.TransactionId).HasColumnName("transaction_id");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
            entity.HasIndex(x => x.TransactionId);
        });
    }
}
