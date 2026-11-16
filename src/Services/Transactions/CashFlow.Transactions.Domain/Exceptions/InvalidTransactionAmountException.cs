namespace CashFlow.Transactions.Domain.Exceptions;

public sealed class InvalidTransactionAmountException() : DomainException("Amount must be positive BRL with at most two decimal places.");
