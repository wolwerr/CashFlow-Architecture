using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CashFlow.Consolidation.Infrastructure.Persistence.Migrations;

[ExcludeFromCodeCoverage]
[DbContext(typeof(ConsolidationDbContext))]
public sealed class ConsolidationDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.3");
        modelBuilder.Entity<DailyBalanceEntity>(entity =>
        {
            entity.ToTable("daily_balances", t =>
            {
                t.HasCheckConstraint("ck_daily_balances_credit", "credit_total >= 0");
                t.HasCheckConstraint("ck_daily_balances_debit", "debit_total >= 0");
            });
            entity.HasKey(e => new { e.MerchantId, e.ReferenceDate });
            entity.Property(e => e.MerchantId).HasColumnName("merchant_id");
            entity.Property(e => e.ReferenceDate).HasColumnName("reference_date").HasColumnType("date");
            entity.Property(e => e.CreditTotal).HasColumnName("credit_total").HasColumnType("numeric(19,2)");
            entity.Property(e => e.DebitTotal).HasColumnName("debit_total").HasColumnType("numeric(19,2)");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamp with time zone");
        });
        modelBuilder.Entity<ProcessedEventEntity>(entity =>
        {
            entity.ToTable("processed_events");
            entity.HasKey(e => e.EventId);
            entity.Property(e => e.EventId).HasColumnName("event_id").ValueGeneratedNever();
            entity.Property(e => e.TransactionId).HasColumnName("transaction_id");
            entity.Property(e => e.MerchantId).HasColumnName("merchant_id");
            entity.Property(e => e.ProcessedAtUtc).HasColumnName("processed_at_utc").HasColumnType("timestamp with time zone");
            entity.HasIndex(e => new { e.MerchantId, e.ProcessedAtUtc });
        });
    }
}
