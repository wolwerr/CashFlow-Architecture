using CashFlow.Consolidation.Application.Gateways;
using CashFlow.Consolidation.Application.UseCases.ProcessTransaction;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Consolidation.Infrastructure.Persistence;

/// <summary>Atomic, multi-instance-safe projection using PostgreSQL ON CONFLICT.</summary>
public sealed class ConsolidationGateway(ConsolidationDbContext db) : IConsolidationGateway
{
    public async Task<bool> ApplyAsync(TransactionRecorded message, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO processed_events (event_id, transaction_id, merchant_id, processed_at_utc)
            VALUES ({message.EventId}, {message.TransactionId}, {message.MerchantId}, {DateTimeOffset.UtcNow})
            ON CONFLICT (event_id) DO NOTHING
            """, cancellationToken);
        if (inserted == 0)
        {
            await tx.CommitAsync(cancellationToken);
            return false;
        }

        var credit = message.Type == "CREDIT" ? message.Amount : 0m;
        var debit = message.Type == "DEBIT" ? message.Amount : 0m;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO daily_balances
                (merchant_id, reference_date, credit_total, debit_total, updated_at_utc)
            VALUES ({message.MerchantId}, {message.ReferenceDate}, {credit}, {debit}, {DateTimeOffset.UtcNow})
            ON CONFLICT (merchant_id, reference_date) DO UPDATE
            SET credit_total = daily_balances.credit_total + EXCLUDED.credit_total,
                debit_total = daily_balances.debit_total + EXCLUDED.debit_total,
                updated_at_utc = EXCLUDED.updated_at_utc
            """, cancellationToken);

        await tx.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<(decimal Credit, decimal Debit, decimal Closing)> GetDailyAsync(
        Guid merchantId, DateOnly referenceDate, CancellationToken cancellationToken)
    {
        var daily = await db.DailyBalances.AsNoTracking()
            .Where(x => x.MerchantId == merchantId && x.ReferenceDate == referenceDate)
            .Select(x => new { x.CreditTotal, x.DebitTotal })
            .SingleOrDefaultAsync(cancellationToken);
        var previous = await db.DailyBalances.AsNoTracking()
            .Where(x => x.MerchantId == merchantId && x.ReferenceDate <= referenceDate)
            .GroupBy(x => 1)
            .Select(g => new
            {
                Credit = g.Sum(x => x.CreditTotal),
                Debit = g.Sum(x => x.DebitTotal)
            }).SingleOrDefaultAsync(cancellationToken);
        return (daily?.CreditTotal ?? 0m, daily?.DebitTotal ?? 0m,
            (previous?.Credit ?? 0m) - (previous?.Debit ?? 0m));
    }
}
