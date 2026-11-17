namespace CashFlow.Transactions.Application.Gateways;

/// <summary>Must verify actual actor/merchant membership and permission, failing closed on uncertainty.</summary>
public interface ICreateTransactionAuthorizationGateway
{
    Task<bool> CanCreateAsync(Guid actorId, Guid merchantId, CancellationToken cancellationToken);
}
