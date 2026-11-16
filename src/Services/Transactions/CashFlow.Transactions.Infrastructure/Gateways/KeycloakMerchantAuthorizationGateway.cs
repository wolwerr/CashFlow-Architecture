using CashFlow.Transactions.Application.Gateways;
using CashFlow.Security.Keycloak;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CashFlow.Transactions.Infrastructure.Gateways;

/// <summary>Service-specific Application port adapter for the shared Keycloak group client.</summary>
public sealed class KeycloakMerchantAuthorizationGateway(
    HttpClient http, IMemoryCache tokens, IOptions<KeycloakAuthorizationSettings> settings)
    : ICreateTransactionAuthorizationGateway
{
    private readonly KeycloakGroupAuthorizationClient keycloak = new(http, tokens, settings);

    public Task<bool> CanCreateAsync(Guid actorId, Guid merchantId,
        CancellationToken cancellationToken) =>
        keycloak.CanAccessAsync(actorId, merchantId, cancellationToken);
}
