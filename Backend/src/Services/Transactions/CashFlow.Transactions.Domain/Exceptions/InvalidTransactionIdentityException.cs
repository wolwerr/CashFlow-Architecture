namespace CashFlow.Transactions.Domain.Exceptions;

public sealed class InvalidTransactionIdentityException(string field) : DomainException($"{field} is required.");
