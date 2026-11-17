namespace CashFlow.Transactions.Application.Exceptions;

/// <summary>Raised by registration implementations for same merchant/key with different payload.</summary>
public sealed class TransactionIdempotencyConflictException()
    : ApplicationLayerException("Idempotency-Key was already used with a different transaction payload.");
