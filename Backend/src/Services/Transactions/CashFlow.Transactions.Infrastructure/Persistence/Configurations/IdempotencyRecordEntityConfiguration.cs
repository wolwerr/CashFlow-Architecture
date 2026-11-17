using CashFlow.Transactions.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CashFlow.Transactions.Infrastructure.Persistence.Configurations;

public sealed class IdempotencyRecordEntityConfiguration : IEntityTypeConfiguration<IdempotencyRecordEntity>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecordEntity> entity)
    {
        entity.ToTable("idempotency_records");
        entity.HasKey(x => new { x.MerchantId, x.RequestKey });
        entity.Property(x => x.MerchantId).HasColumnName("merchant_id");
        entity.Property(x => x.RequestKey).HasColumnName("request_key").HasMaxLength(128);
        entity.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(64).IsRequired();
        entity.Property(x => x.TransactionId).HasColumnName("transaction_id");
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        entity.HasIndex(x => x.TransactionId);
    }
}
