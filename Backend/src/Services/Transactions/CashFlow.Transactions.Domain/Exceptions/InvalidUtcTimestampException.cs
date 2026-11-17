namespace CashFlow.Transactions.Domain.Exceptions;

public sealed class InvalidUtcTimestampException() : DomainException("Timestamp must be UTC.");
