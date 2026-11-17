namespace CashFlow.Transactions.Application.Exceptions;

public sealed class TransactionAuthorizationException()
    : ApplicationLayerException("The actor is not authorized to create a transaction for this merchant.");
