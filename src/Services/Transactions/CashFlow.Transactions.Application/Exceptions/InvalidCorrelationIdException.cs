namespace CashFlow.Transactions.Application.Exceptions;

public sealed class InvalidCorrelationIdException()
    : ApplicationLayerException("Correlation ID must be 1 to 128 characters.");
