namespace CashFlow.Security.Authentication;

/// <summary>
/// JWT trust configuration. Missing or unsafe settings prevent the service from starting.
/// Issuer must be the exact `iss` claim from Keycloak, not a client-provided value.
/// </summary>
public sealed class CashFlowJwtSettings
{
    public const string SectionName = "Authentication";
    public string Authority { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string? MetadataAddress { get; init; }

    public void Validate(bool isDevelopment)
    {
        if (!Uri.TryCreate(Authority, UriKind.Absolute, out var issuer) ||
            issuer.Scheme is not ("https" or "http") ||
            !string.IsNullOrEmpty(issuer.Query) ||
            !string.IsNullOrEmpty(issuer.Fragment) ||
            !Authority.Contains("/realms/", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Authentication:Authority must be a valid Keycloak realm issuer URI.");

        if (!isDevelopment && issuer.Scheme != "https")
            throw new InvalidOperationException("Authentication:Authority must use HTTPS outside Development.");

        if (string.IsNullOrWhiteSpace(Audience) || Audience.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("Authentication:Audience must name the expected API audience.");

        if (MetadataAddress is not null)
        {
            if (!Uri.TryCreate(MetadataAddress, UriKind.Absolute, out var metadata) ||
                metadata.Scheme is not ("https" or "http") ||
                !metadata.AbsolutePath.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Authentication:MetadataAddress must be a valid OIDC discovery endpoint.");
            if (!isDevelopment && metadata.Scheme != "https")
                throw new InvalidOperationException(
                    "Authentication:MetadataAddress must use HTTPS outside Development.");
        }
    }
}
