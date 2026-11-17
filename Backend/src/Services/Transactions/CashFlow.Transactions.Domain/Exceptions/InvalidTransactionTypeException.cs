namespace CashFlow.Transactions.Domain.Exceptions;

public sealed class InvalidTransactionTypeException() : DomainException("Transaction type must be CREDIT or DEBIT.");
