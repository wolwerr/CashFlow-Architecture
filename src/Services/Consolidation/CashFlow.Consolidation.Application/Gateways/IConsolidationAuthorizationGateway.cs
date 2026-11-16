namespace CashFlow.Consolidation.Application.Gateways;

/// <summary>Only authorized merchant-scoped queries are allowed; Keycloak is the source of truth.</summary>
public interface IConsolidationAuthorizationGateway
{
    Task<bool> CanReadAsync(Guid actorId, Guid merchantId, CancellationToken cancellationToken);
}
