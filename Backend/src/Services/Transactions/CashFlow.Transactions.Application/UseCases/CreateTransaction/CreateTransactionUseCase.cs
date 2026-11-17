using CashFlow.Transactions.Application.Exceptions;
using CashFlow.Transactions.Application.Gateways;
using CashFlow.Transactions.Application.UseCases.CreateTransaction.Commands;
using CashFlow.Transactions.Application.UseCases.CreateTransaction.Results;
using CashFlow.Transactions.Domain.Models;

namespace CashFlow.Transactions.Application.UseCases.CreateTransaction;

/// <summary>Orchestrates permission, domain validation and atomic persistence; no HTTP/EF/broker dependency.</summary>
public sealed class CreateTransactionUseCase(
    ICreateTransactionAuthorizationGateway authorization,
    ITransactionRegistrationGateway registration,
    TimeProvider timeProvider)
{
    public async Task<CreateTransactionResult> ExecuteAsync(
        CreateTransactionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Length > 128)
            throw new InvalidIdempotencyKeyException();
        if (string.IsNullOrWhiteSpace(command.CorrelationId) || command.CorrelationId.Length > 128)
            throw new InvalidCorrelationIdException();

        // An unavailable/uncertain authorization gateway must fail closed and not call persistence.
        if (!await authorization.CanCreateAsync(command.ActorId, command.MerchantId, cancellationToken))
            throw new TransactionAuthorizationException();

        var now = timeProvider.GetUtcNow();
        var today = BusinessCalendar.CurrentDate(now);
        var transaction = Transaction.Create(command.MerchantId, command.ActorId, command.Type,
            command.Amount, command.ReferenceDate, today, now);

        return await registration.RegisterAsync(transaction, command.IdempotencyKey,
            command.CorrelationId, cancellationToken);
    }
}
