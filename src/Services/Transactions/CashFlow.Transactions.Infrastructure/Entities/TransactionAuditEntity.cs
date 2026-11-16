namespace CashFlow.Transactions.Infrastructure.Entities;

public sealed class TransactionAuditEntity
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public Guid MerchantId { get; set; }
    public Guid ActorId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
}
