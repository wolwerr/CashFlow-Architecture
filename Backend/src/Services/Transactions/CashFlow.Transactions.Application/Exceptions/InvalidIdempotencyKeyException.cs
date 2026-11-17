namespace CashFlow.Transactions.Application.Exceptions;

public sealed class InvalidIdempotencyKeyException()
    : ApplicationLayerException("A valid Idempotency-Key of 1 to 128 characters is required.");
