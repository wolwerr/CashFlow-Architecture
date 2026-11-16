namespace CashFlow.Transactions.Infrastructure.Entities;

public sealed class IdempotencyRecordEntity
{
    public Guid MerchantId { get; set; }
    public string RequestKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public Guid TransactionId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
