using CashFlow.Transactions.Domain.Enums;

namespace CashFlow.Transactions.Infrastructure.Entities;

public sealed class TransactionEntity
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public Guid ActorId { get; set; }
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public DateOnly ReferenceDate { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
