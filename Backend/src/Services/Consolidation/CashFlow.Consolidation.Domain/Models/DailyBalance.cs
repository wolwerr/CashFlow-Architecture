namespace CashFlow.Consolidation.Domain.Models;

/// <summary>Only per-day movements are stored. Closing balance is derived at query time.</summary>
public sealed class DailyBalance
{
    public Guid MerchantId { get; private set; }
    public DateOnly ReferenceDate { get; private set; }
    public decimal CreditTotal { get; private set; }
    public decimal DebitTotal { get; private set; }

    public DailyBalance(Guid merchantId, DateOnly referenceDate)
    {
        if (merchantId == Guid.Empty) throw new ArgumentException("Merchant ID required.", nameof(merchantId));
        MerchantId = merchantId;
        ReferenceDate = referenceDate;
    }

    public void Apply(string type, decimal amount)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount));
        switch (type)
        {
            case "CREDIT":
                CreditTotal = checked(CreditTotal + amount);
                break;
            case "DEBIT":
                DebitTotal = checked(DebitTotal + amount);
                break;
            default:
                throw new ArgumentException("Unsupported transaction type.", nameof(type));
        }
    }

    public decimal NetAmount => CreditTotal - DebitTotal;
}
