using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.Json;
using CashFlow.Consolidation.Application.Gateways;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CashFlow.Consolidation.Infrastructure.Gateways;

/// <summary>Keycloak is the source of truth. No positive merchant-permission caching.</summary>
public sealed class KeycloakConsolidationAuthorizationGateway(
    HttpClient http, IMemoryCache tokens, IOptions<KeycloakConsolidationSettings> configuration)
    : IConsolidationAuthorizationGateway
{
    private readonly KeycloakConsolidationSettings settings = configuration.Value;
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private const int PageSize = 100;
    private const int MaxPages = 20;

    public async Task<bool> CanReadAsync(Guid actorId, Guid merchantId,
        CancellationToken cancellationToken)
    {
        if (actorId == Guid.Empty || merchantId == Guid.Empty)
            return false;

        try
        {
            var bearer = await GetAccessTokenAsync(cancellationToken);
            // Never cache membership: a revoked role must be checked against Keycloak.
            for (var page = 0; page < MaxPages; page++)
            {
                var groups = await ReadGroupPageAsync(
                    actorId, bearer, page, cancellationToken);
                if (groups is null)
                    return false; // Keycloak reports the user does not exist.
                if (groups.Any(group => MatchesRole(group.Path, merchantId)))
                    return true;
                if (groups.Count < PageSize)
                    return false;
            }
            throw new MerchantAuthorizationUnavailableException(
                "Keycloak group page limit exceeded.");
        }
        catch (HttpRequestException ex)
        {
            throw new MerchantAuthorizationUnavailableException(
                "Keycloak network request failed.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MerchantAuthorizationUnavailableException(
                "Keycloak request timed out.", ex);
        }
        catch (JsonException ex)
        {
            throw new MerchantAuthorizationUnavailableException(
                "Keycloak response is invalid.", ex);
        }
    }

    private static bool MatchesRole(string? path, Guid merchantId) =>
        string.Equals(path, $"/merchants/{merchantId:D}/ADMIN", StringComparison.Ordinal) ||
        string.Equals(path, $"/merchants/{merchantId:D}/OPERATOR", StringComparison.Ordinal);

    private async Task<List<GroupView>?> ReadGroupPageAsync(Guid actorId,
        string bearer, int page, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"admin/realms/{Uri.EscapeDataString(settings.Realm)}/users/{actorId:D}/groups?first={page * PageSize}&max={PageSize}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await http.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new MerchantAuthorizationUnavailableException("Keycloak group lookup failed.");

        return await response.Content.ReadFromJsonAsync<List<GroupView>>(
            cancellationToken: cancellationToken)
            ?? throw new MerchantAuthorizationUnavailableException("Keycloak group response missing.");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var key = $"keycloak-token:{settings.Realm}:{settings.ClientId}";
        if (tokens.TryGetValue<string>(key, out var cached) && cached is not null)
            return cached;

        await tokenLock.WaitAsync(ct);
        try
        {
            if (tokens.TryGetValue<string>(key, out cached) && cached is not null)
                return cached;

            using var response = await http.PostAsync(
                $"realms/{Uri.EscapeDataString(settings.Realm)}/protocol/openid-connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = settings.ClientId,
                    ["client_secret"] = settings.ClientSecret
                }), ct);
            if (!response.IsSuccessStatusCode)
                throw new MerchantAuthorizationUnavailableException("Keycloak service token unavailable.");

            var token = await response.Content.ReadFromJsonAsync<ServiceToken>(cancellationToken: ct)
                ?? throw new MerchantAuthorizationUnavailableException("Keycloak token response missing.");
            if (string.IsNullOrWhiteSpace(token.AccessToken) || token.ExpiresIn <= 0)
                throw new MerchantAuthorizationUnavailableException("Keycloak token response invalid.");

            tokens.Set(key, token.AccessToken, TimeSpan.FromSeconds(Math.Max(1, token.ExpiresIn - 30)));
            return token.AccessToken;
        }
        finally { tokenLock.Release(); }
    }

    private sealed record GroupView([property: JsonPropertyName("path")] string? Path);
    private sealed record ServiceToken(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

public sealed class MerchantAuthorizationUnavailableException : Exception
{
    public MerchantAuthorizationUnavailableException(string message) : base(message) { }
    public MerchantAuthorizationUnavailableException(string message, Exception inner) : base(message, inner) { }
}

public sealed class KeycloakConsolidationSettings
{
    public const string SectionName = "KeycloakAuthorization";
    public string BaseUrl { get; set; } = string.Empty;
    public string Realm { get; set; } = "cashflow";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    public void Validate(bool development)
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            (!development && uri.Scheme != "https") ||
            (uri.Scheme != "https" && uri.Scheme != "http") ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("KeycloakAuthorization:BaseUrl must be a trusted absolute URI, HTTPS outside Development.");
        if (string.IsNullOrWhiteSpace(Realm) || Realm.Contains('/') ||
            string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException("Keycloak authorization service credentials and realm are required.");
    }
}
