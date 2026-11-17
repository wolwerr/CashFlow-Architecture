using CashFlow.Consolidation.Application.Gateways;
using CashFlow.Security.Keycloak;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CashFlow.Consolidation.Infrastructure.Gateways;

/// <summary>Service-specific Application port adapter for the shared Keycloak group client.</summary>
public sealed class KeycloakConsolidationAuthorizationGateway(
    HttpClient http, IMemoryCache tokens, IOptions<KeycloakAuthorizationSettings> settings)
    : IConsolidationAuthorizationGateway
{
    private readonly KeycloakGroupAuthorizationClient keycloak = new(http, tokens, settings);

    public Task<bool> CanReadAsync(Guid actorId, Guid merchantId,
        CancellationToken cancellationToken) =>
        keycloak.CanAccessAsync(actorId, merchantId, cancellationToken);
}
