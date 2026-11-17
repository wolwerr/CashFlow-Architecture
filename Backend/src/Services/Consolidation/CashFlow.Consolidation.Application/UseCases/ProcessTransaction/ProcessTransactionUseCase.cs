using CashFlow.Consolidation.Application.Gateways;

namespace CashFlow.Consolidation.Application.UseCases.ProcessTransaction;

public sealed class ProcessTransactionUseCase(IConsolidationGateway gateway)
{
    public Task<bool> ExecuteAsync(TransactionRecorded transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.EventId == Guid.Empty || transaction.TransactionId == Guid.Empty ||
            transaction.MerchantId == Guid.Empty || transaction.Amount <= 0 ||
            decimal.Round(transaction.Amount, 2) != transaction.Amount ||
            transaction.Type is not ("CREDIT" or "DEBIT") ||
            transaction.CreatedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Invalid transaction recorded event.");
        return gateway.ApplyAsync(transaction, cancellationToken);
    }
}
