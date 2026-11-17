using CashFlow.Consolidation.Application.UseCases.ProcessTransaction;

namespace CashFlow.Consolidation.Application.Gateways;

public interface IConsolidationGateway
{
    /// <returns>True on first application; false when event_id was already processed.</returns>
    Task<bool> ApplyAsync(TransactionRecorded message, CancellationToken cancellationToken);
    Task<(decimal Credit, decimal Debit, decimal Closing)> GetDailyAsync(
        Guid merchantId, DateOnly referenceDate, CancellationToken cancellationToken);
}
