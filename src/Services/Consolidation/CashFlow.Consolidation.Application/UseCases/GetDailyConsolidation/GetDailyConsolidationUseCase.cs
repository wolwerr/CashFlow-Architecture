using CashFlow.Consolidation.Application.Gateways;

namespace CashFlow.Consolidation.Application.UseCases.GetDailyConsolidation;

public sealed class GetDailyConsolidationUseCase(IConsolidationGateway gateway)
{
    public Task<(decimal Credit, decimal Debit, decimal Closing)> ExecuteAsync(
        Guid merchantId, DateOnly date, CancellationToken cancellationToken)
    {
        if (merchantId == Guid.Empty)
            throw new ArgumentException("Merchant ID is required.", nameof(merchantId));
        return gateway.GetDailyAsync(merchantId, date, cancellationToken);
    }
}
