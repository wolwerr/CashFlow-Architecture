namespace CashFlow.Transactions.Domain;

/// <summary>Immutable financial record; authorization and persistence are handled outside the domain.</summary>
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
        if (merchantId == Guid.Empty) throw new ArgumentException("Merchant is required.", nameof(merchantId));
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive BRL with at most two decimal places.");
        if (referenceDate > currentBusinessDate)
            throw new ArgumentOutOfRangeException(nameof(referenceDate), "Future reference dates are not permitted.");
        if (createdAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Creation timestamp must be UTC.", nameof(createdAtUtc));
        return new Transaction(Guid.NewGuid(), merchantId, actorId, type, amount, referenceDate, createdAtUtc);
    }
}
