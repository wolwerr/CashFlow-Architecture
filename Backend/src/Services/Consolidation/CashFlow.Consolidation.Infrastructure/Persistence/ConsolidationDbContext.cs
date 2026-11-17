using Microsoft.EntityFrameworkCore;

namespace CashFlow.Consolidation.Infrastructure.Persistence;

public sealed class ConsolidationDbContext(DbContextOptions<ConsolidationDbContext> options) : DbContext(options)
{
    public DbSet<DailyBalanceEntity> DailyBalances => Set<DailyBalanceEntity>();
    public DbSet<ProcessedEventEntity> ProcessedEvents => Set<ProcessedEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

public sealed class DailyBalanceEntity
{
    public Guid MerchantId { get; set; }
    public DateOnly ReferenceDate { get; set; }
    public decimal CreditTotal { get; set; }
    public decimal DebitTotal { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ProcessedEventEntity
{
    public Guid EventId { get; set; }
    public Guid TransactionId { get; set; }
    public Guid MerchantId { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
}
