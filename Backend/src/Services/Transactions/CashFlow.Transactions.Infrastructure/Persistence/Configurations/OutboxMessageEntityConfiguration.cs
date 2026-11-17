using CashFlow.Transactions.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CashFlow.Transactions.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageEntityConfiguration : IEntityTypeConfiguration<OutboxMessageEntity>
{
    public void Configure(EntityTypeBuilder<OutboxMessageEntity> entity)
    {
        entity.ToTable("outbox_messages", t => t.HasCheckConstraint("ck_outbox_attempts", "attempt_count >= 0"));
        entity.HasKey(x => x.EventId);
        entity.Property(x => x.EventId).HasColumnName("event_id").ValueGeneratedNever();
        entity.Property(x => x.MerchantId).HasColumnName("merchant_id").IsRequired();
        entity.Property(x => x.TransactionId).HasColumnName("transaction_id").IsRequired();
        entity.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(128).IsRequired();
        entity.Property(x => x.SchemaVersion).HasColumnName("schema_version").HasMaxLength(16).IsRequired();
        entity.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
        entity.Property(x => x.TraceParent).HasColumnName("trace_parent").HasMaxLength(55);
        entity.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        entity.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc").HasColumnType("timestamp with time zone");
        entity.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0);
        entity.HasIndex(x => new { x.PublishedAtUtc, x.CreatedAtUtc });
        entity.HasIndex(x => new { x.MerchantId, x.TransactionId });
    }
}
