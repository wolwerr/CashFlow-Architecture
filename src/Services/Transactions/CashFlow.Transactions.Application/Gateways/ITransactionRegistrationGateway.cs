using CashFlow.Transactions.Application.UseCases.CreateTransaction.Results;
using CashFlow.Transactions.Domain.Models;

namespace CashFlow.Transactions.Application.Gateways;

/// <summary>
/// Contract for atomic registration of Transaction + Outbox + confirmed audit + HTTP idempotency state.
/// Implementations must return only after successful commit; same merchant/key/payload replays the
/// stored result and a conflicting payload must raise an application conflict. Never return a partial commit.
/// </summary>
public interface ITransactionRegistrationGateway
{
    Task<CreateTransactionResult> RegisterAsync(
        Transaction transaction,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken);
}
