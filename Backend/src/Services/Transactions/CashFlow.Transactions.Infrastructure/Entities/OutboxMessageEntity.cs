namespace CashFlow.Transactions.Infrastructure.Entities;

public sealed class OutboxMessageEntity
{
    public Guid EventId { get; set; }
    public Guid MerchantId { get; set; }
    public Guid TransactionId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    // W3C trace context captured at request commit; never an authentication signal.
    public string? TraceParent { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public int AttemptCount { get; set; }
}
