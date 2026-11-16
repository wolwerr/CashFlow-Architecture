using CashFlow.Transactions.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CashFlow.Transactions.Infrastructure.Persistence.Configurations;

public sealed class TransactionAuditEntityConfiguration : IEntityTypeConfiguration<TransactionAuditEntity>
{
    public void Configure(EntityTypeBuilder<TransactionAuditEntity> entity)
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
    }
}
