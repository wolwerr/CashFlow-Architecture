using CashFlow.Transactions.Domain.Enums;
using CashFlow.Transactions.Domain.Exceptions;

namespace CashFlow.Transactions.Domain.Models;

/// <summary>Immutable financial record; persistence and authorization are outside Domain.</summary>
public sealed class Transaction
{
    private Transaction(Guid id, Guid merchantId, Guid actorId, TransactionType type,
        decimal amount, DateOnly referenceDate, DateTimeOffset createdAtUtc)
    {
        Id = id;
        MerchantId = merchantId;
        ActorId = actorId;
        Type = type;
        Amount = amount;
        ReferenceDate = referenceDate;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }
    public Guid MerchantId { get; }
    public Guid ActorId { get; }
    public TransactionType Type { get; }
    public decimal Amount { get; }
    public DateOnly ReferenceDate { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public decimal SignedAmount => Type == TransactionType.Credit ? Amount : -Amount;

    public static Transaction Create(Guid merchantId, Guid actorId, TransactionType type,
        decimal amount, DateOnly referenceDate, DateOnly currentBusinessDate, DateTimeOffset createdAtUtc)
    {
        if (merchantId == Guid.Empty) throw new InvalidTransactionIdentityException("Merchant");
        if (actorId == Guid.Empty) throw new InvalidTransactionIdentityException("Actor");
        if (!Enum.IsDefined(type)) throw new InvalidTransactionTypeException();
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new InvalidTransactionAmountException();
        if (referenceDate > currentBusinessDate)
            throw new FutureReferenceDateException();
        if (createdAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidUtcTimestampException();
        return new Transaction(Guid.NewGuid(), merchantId, actorId, type, amount, referenceDate, createdAtUtc);
    }
}
