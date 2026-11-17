namespace CashFlow.Transactions.Domain.Exceptions;

public sealed class FutureReferenceDateException() : DomainException("Future reference dates are not permitted.");
