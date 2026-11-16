namespace CashFlow.Transactions.Infrastructure.Exceptions;

/// <summary>Represents a technical failure while persisting a financial registration.</summary>
public sealed class TransactionPersistenceException(string message, Exception innerException)
    : InfrastructureLayerException(message, innerException);
