using CashFlow.Transactions.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CashFlow.Transactions.Infrastructure.Persistence.Configurations;

public sealed class TransactionEntityConfiguration : IEntityTypeConfiguration<TransactionEntity>
{
    public void Configure(EntityTypeBuilder<TransactionEntity> entity)
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
    }
}
