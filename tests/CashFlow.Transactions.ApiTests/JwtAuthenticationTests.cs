using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using CashFlow.Transactions.Api.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CashFlow.Transactions.ApiTests;

public sealed class JwtAuthenticationTests : IDisposable
{
    private const string Issuer = "http://localhost:8081/realms/cashflow";
    private const string Audience = "cashflow-transactions-api";
    private readonly string? _priorAuthority;
    private readonly string? _priorAudience;
    private readonly Dictionary<string, string?> _original = new();
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly WebApplicationFactory<Program> _factory;

    public JwtAuthenticationTests()
    {
        // Minimal-hosting WebApplication validates configuration during Program startup,
        // before WebApplicationFactory.ConfigureAppConfiguration can amend it.
        _priorAuthority = Environment.GetEnvironmentVariable("Authentication__Authority");
        _priorAudience = Environment.GetEnvironmentVariable("Authentication__Audience");
        Environment.SetEnvironmentVariable("Authentication__Authority", Issuer);
        Environment.SetEnvironmentVariable("Authentication__Audience", Audience);
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["KeycloakAuthorization__BaseUrl"] = "http://localhost:8081",
            ["KeycloakAuthorization__Realm"] = "cashflow",
            ["KeycloakAuthorization__ClientId"] = "test-authz-client",
            ["KeycloakAuthorization__ClientSecret"] = "unit-test-not-a-secret",
            ["ConnectionStrings__Transactions"] = "Host=localhost;Database=unused;Username=unused;Password=unused"
        })
        {
            _original[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }
        _key = new RsaSecurityKey(_rsa) { KeyId = "test-key" };
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Development");
                builder.ConfigureTestServices(services =>
                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    {
                        var oidc = new OpenIdConnectConfiguration { Issuer = Issuer };
                        oidc.SigningKeys.Add(_key);
                        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(oidc);
                    }));
            });
    }

    [Fact]
    public async Task HealthIsPublic()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MissingBearerTokenIsUnauthorized()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/internal/authentication/check");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongAuthenticationSchemeIsUnauthorized()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "irrelevant");
        var response = await client.GetAsync("/internal/authentication/check");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidSignedTokenWithExpectedIssuerAndAudiencePasses()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        var response = await client.GetAsync("/internal/authentication/check");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData("http://attacker.invalid/realms/cashflow", Audience)]
    [InlineData(Issuer, "other-api")]
    public async Task InvalidIssuerOrAudienceIsUnauthorized(string issuer, string audience)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(issuer, audience));
        var response = await client.GetAsync("/internal/authentication/check");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredTokenIsUnauthorized()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            Token(notBefore: DateTime.UtcNow.AddHours(-2), expires: DateTime.UtcNow.AddHours(-1)));
        var response = await client.GetAsync("/internal/authentication/check");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenSignedByUntrustedKeyIsUnauthorized()
    {
        using var otherRsa = RSA.Create(2048);
        var otherKey = new RsaSecurityKey(otherRsa);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(signingKey: otherKey));
        var response = await client.GetAsync("/internal/authentication/check");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenWithoutSignatureIsUnauthorized()
    {
        var token = new JwtSecurityToken(Issuer, Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddMinutes(5));
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new JwtSecurityTokenHandler().WriteToken(token));
        var response = await client.GetAsync("/internal/authentication/check");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private string Token(
        string issuer = Issuer, string audience = Audience,
        DateTime? notBefore = null, DateTime? expires = null, SecurityKey? signingKey = null)
    {
        var token = new JwtSecurityToken(
            issuer, audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            notBefore: notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            expires: expires ?? DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(signingKey ?? _key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose()
    {
        _factory.Dispose();
        Environment.SetEnvironmentVariable("Authentication__Authority", _priorAuthority);
        Environment.SetEnvironmentVariable("Authentication__Audience", _priorAudience);
        foreach (var item in _original)
            Environment.SetEnvironmentVariable(item.Key, item.Value);
        _rsa.Dispose();
    }
}
