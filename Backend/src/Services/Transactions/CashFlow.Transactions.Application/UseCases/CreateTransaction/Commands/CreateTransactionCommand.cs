using CashFlow.Transactions.Domain.Enums;

namespace CashFlow.Transactions.Application.UseCases.CreateTransaction.Commands;

/// <summary>Authenticated actor ID comes from a trusted API boundary, not the request body.</summary>
public sealed record CreateTransactionCommand(
    Guid MerchantId,
    Guid ActorId,
    TransactionType Type,
    decimal Amount,
    DateOnly ReferenceDate,
    string IdempotencyKey,
    string CorrelationId);
