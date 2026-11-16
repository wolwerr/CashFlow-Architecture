using CashFlow.Transactions.Domain.Enums;

namespace CashFlow.Transactions.Application.UseCases.CreateTransaction.Results;

/// <summary>Only a committed record may be returned by the persistence gateway.</summary>
public sealed record CreateTransactionResult(
    Guid Id, Guid MerchantId, TransactionType Type, decimal Amount,
    DateOnly ReferenceDate, DateTimeOffset CreatedAtUtc);
