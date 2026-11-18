using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using CashFlow.Consolidation.Application.Gateways;
using CashFlow.Consolidation.Application.UseCases.ProcessTransaction;
using CashFlow.Security.Keycloak;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CashFlow.Consolidation.ApiTests;

public sealed class DailyConsolidationApiTests : IDisposable
{
    private const string Issuer = "http://localhost:8081/realms/cashflow";
    private const string Audience = "cashflow-consolidation-api";
    private readonly RSA rsa = RSA.Create(2048);
    private readonly RsaSecurityKey key;
    private readonly WebApplicationFactory<Program> factory;
    private readonly Dictionary<string, string?> originals = new();
    private readonly StubAuthorization authorization = new();
    private readonly StubGateway gateway = new();

    public DailyConsolidationApiTests()
    {
        foreach (var item in new Dictionary<string, string>
        {
            ["Authentication__Authority"] = Issuer,
            ["Authentication__Audience"] = Audience,
            ["KeycloakAuthorization__BaseUrl"] = "http://localhost:8081",
            ["KeycloakAuthorization__Realm"] = "cashflow",
            ["KeycloakAuthorization__ClientId"] = "test-client",
            ["KeycloakAuthorization__ClientSecret"] = "test-only",
            ["ConnectionStrings__Consolidation"] = "Host=localhost;Database=unused;Username=unused;Password=unused",
            ["RabbitMq__Host"] = "localhost",
            ["RabbitMq__User"] = "test",
            ["RabbitMq__Password"] = "test",
            ["Consolidation__EnableConsumer"] = "false",
            ["Consolidation__ApplyMigrationsOnStartup"] = "false"
        })
        {
            originals[item.Key] = Environment.GetEnvironmentVariable(item.Key);
            Environment.SetEnvironmentVariable(item.Key, item.Value);
        }
        key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Development");
            b.ConfigureTestServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opts =>
                {
                    var config = new OpenIdConnectConfiguration { Issuer = Issuer };
                    config.SigningKeys.Add(key);
                    opts.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(config);
                });
                services.RemoveAll<IConsolidationAuthorizationGateway>();
                services.RemoveAll<IConsolidationGateway>();
                services.AddSingleton<IConsolidationAuthorizationGateway>(authorization);
                services.AddSingleton<IConsolidationGateway>(gateway);
            });
        });
    }

    [Fact]
    public async Task SwaggerDocumentsDailyConsolidationAndBearerSecurity()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/v1/consolidation/daily", body);
        Assert.Contains("\"Bearer\"", body);
        Assert.Contains("\"security\"", body);
        Assert.Contains("\"bearer\"", body);
        Assert.Contains("\"KeycloakOAuth2\"", body);
        Assert.Contains("\"authorizationCode\"", body);
        Assert.Contains("http://localhost:8081/realms/cashflow/protocol/openid-connect/auth", body);
        Assert.Contains("http://localhost:8081/realms/cashflow/protocol/openid-connect/token", body);
        Assert.Contains("\"openid\"", body);
    }

    [Fact]
    public async Task SwaggerUiIsAvailableInDevelopment()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        using var redirect = await client.GetAsync("/swagger/oauth2-redirect.html");
        Assert.Equal(HttpStatusCode.OK, redirect.StatusCode);
    }

    [Fact]
    public async Task HealthIsPublicAndBalanceRequiresJwt()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url(Guid.NewGuid()))).StatusCode);
        Assert.Equal(0, gateway.Reads);
    }

    [Fact]
    public async Task AuthorizedMerchantReturnsEmptyDayAndAccumulatedBalance()
    {
        using var client = ClientWithToken();
        var merchant = Guid.NewGuid();
        using var response = await client.GetAsync(Url(merchant));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"closing_balance\":\"25.50\"", body);
        Assert.Contains("\"credit_total\":\"30.50\"", body);
        Assert.Contains("\"debit_total\":\"5.00\"", body);
        Assert.Contains($"\"merchant_id\":\"{merchant:D}\"", body);
        Assert.Equal(merchant, authorization.LastMerchant);
        Assert.Equal(1, gateway.Reads);
    }

    [Fact]
    public async Task MissingQueryParametersReturnsBadRequest()
    {
        using var client = ClientWithToken();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/v1/consolidation/daily")).StatusCode);
        Assert.Equal(0, gateway.Reads);
    }

    [Fact]
    public async Task UnrelatedMerchantReturns403WithoutDatabaseAccess()
    {
        authorization.Allowed = false;
        using var client = ClientWithToken();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync(Url(Guid.NewGuid()))).StatusCode);
        Assert.Equal(0, gateway.Reads);
    }

    [Fact]
    public async Task AuthorizationFailureReturns503WithoutDatabaseAccess()
    {
        authorization.Outage = true;
        using var client = ClientWithToken();
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.GetAsync(Url(Guid.NewGuid()))).StatusCode);
        Assert.Equal(0, gateway.Reads);
    }

    private HttpClient ClientWithToken()
    {
        var client = factory.CreateClient();
        var token = new JwtSecurityToken(Issuer, Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString("D"))],
            notBefore: DateTime.UtcNow.AddSeconds(-10),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    private static string Url(Guid merchant) =>
        $"/api/v1/consolidation/daily?merchant_id={merchant:D}&date=2026-11-15";

    public void Dispose()
    {
        factory.Dispose();
        rsa.Dispose();
        foreach (var item in originals)
            Environment.SetEnvironmentVariable(item.Key, item.Value);
    }

    private sealed class StubAuthorization : IConsolidationAuthorizationGateway
    {
        public bool Allowed { get; set; } = true;
        public bool Outage { get; set; }
        public Guid LastMerchant { get; private set; }

        public Task<bool> CanReadAsync(Guid actor, Guid merchant, CancellationToken ct)
        {
            LastMerchant = merchant;
            if (Outage) throw new MerchantAuthorizationUnavailableException("Simulated outage.");
            return Task.FromResult(Allowed);
        }
    }

    private sealed class StubGateway : IConsolidationGateway
    {
        public int Reads { get; private set; }
        public Task<bool> ApplyAsync(TransactionRecorded message, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<(decimal Credit, decimal Debit, decimal Closing)> GetDailyAsync(
            Guid merchant, DateOnly date, CancellationToken ct)
        {
            Reads++;
            return Task.FromResult((30.50m, 5m, 25.50m));
        }
    }
}
