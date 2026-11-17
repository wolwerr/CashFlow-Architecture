using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using CashFlow.Transactions.Application.Gateways;
using CashFlow.Transactions.Application.Exceptions;
using CashFlow.Transactions.Application.UseCases.CreateTransaction.Results;
using CashFlow.Transactions.Domain.Models;
using CashFlow.Transactions.Domain.Enums;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using CashFlow.Security.Authentication;
using CashFlow.Transactions.Api.Merchants;
using Microsoft.Extensions.Options;
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
    private readonly MockAuthorization _authorization = new();
    private readonly MockRegistration _registration = new();

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
                {
                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    {
                        var oidc = new OpenIdConnectConfiguration { Issuer = Issuer };
                        oidc.SigningKeys.Add(_key);
                        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(oidc);
                    });
                    services.RemoveAll<ICreateTransactionAuthorizationGateway>();
                    services.RemoveAll<ITransactionRegistrationGateway>();
                    services.AddSingleton<ICreateTransactionAuthorizationGateway>(_authorization);
                    services.AddSingleton<ITransactionRegistrationGateway>(_registration);
                });
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

    [Fact]
    public async Task PostWithValidJwtAndAuthorizedMerchantReturnsCreated()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        var merchant = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions")
        {
            Content = JsonContent.Create(new
            {
                merchant_id = merchant, type = "CREDIT", amount = "10.25",
                reference_date = "2020-01-01"
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.StartsWith("/api/v1/transactions/", response.Headers.Location?.ToString());
        Assert.Equal(1, _registration.Calls);
        Assert.Equal(merchant, _authorization.LastMerchant);
    }

    [Fact]
    public async Task UnauthorizedMerchantDoesNotCallPersistence()
    {
        _authorization.Allowed = false;
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        var response = await Submit(client, "10.25");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, _registration.Calls);
    }

    [Fact]
    public async Task AuthorizationOutageDoesNotCallPersistence()
    {
        _authorization.Outage = true;
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        var response = await Submit(client, "10.25");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, _registration.Calls);
    }

    [Theory]
    [InlineData("0.00")]
    [InlineData("10.251")]
    [InlineData("-1.00")]
    [InlineData("not-a-number")]
    public async Task InvalidAmountIsRejectedBeforePersistence(string amount)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        var response = await Submit(client, amount);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0, _registration.Calls);
    }

    [Fact]
    public async Task ConflictingIdempotencyKeyReturns409()
    {
        _registration.Conflict = true;
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        var response = await Submit(client, "10.25");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, _registration.Calls);
    }

    [Fact]
    public async Task InvalidTransactionTypeReturns400()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions")
        {
            Content = JsonContent.Create(new
            {
                merchant_id = Guid.NewGuid(), type = "TRANSFER", amount = "10.25",
                reference_date = "2020-01-01"
            })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _registration.Calls);
    }

    [Fact]
    public async Task InvalidCorrelationHeaderReturns400()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions")
        {
            Content = JsonContent.Create(new
            {
                merchant_id = Guid.NewGuid(), type = "CREDIT", amount = "10.25",
                reference_date = "2020-01-01"
            })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        req.Headers.TryAddWithoutValidation("X-Correlation-ID", new string('x', 129));
        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _registration.Calls);
    }

    [Fact]
    public async Task FutureReferenceDateReturns422()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions")
        {
            Content = JsonContent.Create(new
            {
                merchant_id = Guid.NewGuid(), type = "DEBIT", amount = "10.25",
                reference_date = "2099-01-01"
            })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0, _registration.Calls);
    }

    [Fact]
    public async Task MissingIdempotencyKeyDoesNotPersist()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        var response = await Submit(client, "10.25", false);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _registration.Calls);
    }

    [Fact]
    public async Task PostWithoutJwtIsUnauthorized()
    {
        using var client = _factory.CreateClient();
        var response = await Submit(client, "10.25");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _registration.Calls);
    }

    [Theory]
    [InlineData("PUT", "/api/v1/merchants/details", "{\"name\":\"  \"}")]
    [InlineData("POST", "/api/v1/merchants/members", "{\"username\":\"x\",\"password\":\"a-secure-password\"}")]
    [InlineData("PUT", "/api/v1/merchants/members", "{\"username\":\"x\",\"role\":\"ADMIN\"}")]
    [InlineData("DELETE", "/api/v1/merchants/members", "{\"username\":\"x\"}")]
    public async Task InvalidMerchantMutationIsRejectedBeforeKeycloak(
        string method, string path, string payload)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token());
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Merchant-ID", Guid.NewGuid().ToString("D"));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/v1/merchants/members")]
    [InlineData("POST", "/api/v1/merchants/members")]
    [InlineData("PUT", "/api/v1/merchants/members")]
    [InlineData("DELETE", "/api/v1/merchants/members")]
    [InlineData("PUT", "/api/v1/merchants/details")]
    public async Task ProtectedMerchantManagementRequiresJwt(string method, string path)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT" or "DELETE")
            request.Content = JsonContent.Create(new { username = "loja.teste", role = "ADMIN", name = "Loja" });
        request.Headers.Add("X-Merchant-ID", Guid.NewGuid().ToString("D"));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MerchantDirectoryWithoutJwtIsUnauthorized()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/v1/merchants/")).StatusCode);
    }

    [Fact]
    public async Task MerchantBootstrapRejectsUnapprovedAuthenticatedUser()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        var response = await client.PostAsJsonAsync("/api/v1/merchants/", new { name = "Loja Campinas" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MerchantBootstrapRejectsInvalidNameBeforeProvisioning()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        var response = await client.PostAsJsonAsync("/api/v1/merchants/", new { name = " " });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminRegistrationRejectsMissingUsername()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        var response = await client.PostAsJsonAsync(
            $"/api/v1/merchants/{Guid.NewGuid():D}/admins", new { username = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task HistoryWithoutJwtIsUnauthorized()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/transactions?merchant_id=" + Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HistoryWithMissingMerchantIsBadRequestBeforeDatabaseQuery()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/v1/transactions")).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100001)]
    public async Task HistoryRejectsInvalidPages(int page)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        var response = await client.GetAsync($"/api/v1/transactions?merchant_id={Guid.NewGuid()}&page={page}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task HistoryDeniedForMerchantWithoutMembership()
    {
        _authorization.Allowed = false;
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        var merchant = Guid.NewGuid();
        var response = await client.GetAsync($"/api/v1/transactions?merchant_id={merchant:D}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(merchant, _authorization.LastMerchant);
    }

    [Fact]
    public async Task HistoryDeniesOnKeycloakOutage()
    {
        _authorization.Outage = true;
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        var response = await client.GetAsync($"/api/v1/transactions?merchant_id={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("add", 204)]
    [InlineData("role", 204)]
    [InlineData("remove", 204)]
    [InlineData("rename", 204)]
    [InlineData("renameblocked", 409)]
    public async Task AdminMerchantMutationsExerciseRealHttpEndpointContracts(
        string operation, int expectedStatus)
    {
        var merchant = Guid.NewGuid();
        var member = Guid.NewGuid();
        var handler = new MerchantManagementStub(merchant, member, operation == "renameblocked");
        using var keycloak = new HttpClient(handler) { BaseAddress = new Uri("http://keycloak:8080/") };
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<KeycloakMerchantManagement>();
                services.AddSingleton(new KeycloakMerchantManagement(keycloak,
                    Options.Create(new KeycloakManagementSettings
                    {
                        Realm = "cashflow", ClientId = "management", ClientSecret = "local-test"
                    })));
            }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        var path = operation switch
        {
            "rename" or "renameblocked" => "/api/v1/merchants/members/username",
            _ => "/api/v1/merchants/members"
        };
        var method = operation switch
        {
            "add" => HttpMethod.Post,
            "remove" => HttpMethod.Delete,
            _ => HttpMethod.Put
        };
        object body = operation switch
        {
            "add" => new { username = "new.operator", password = "long-test-password" },
            "role" => new { username = "old.operator", role = "ADMIN" },
            "remove" => new { username = "old.operator" },
            _ => new { username = "old.operator", newUsername = "new.name" }
        };
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Merchant-ID", merchant.ToString("D"));
        using var response = await client.SendAsync(request);
        Assert.Equal((HttpStatusCode)expectedStatus, response.StatusCode);
        Assert.True(handler.ReceivedManagementRequest);
    }

    private sealed class MerchantManagementStub(Guid merchant, Guid member, bool denyRename)
        : HttpMessageHandler
    {
        private bool created;

        public bool ReceivedManagementRequest { get; private set; }

        private static HttpResponseMessage Json(object body) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

        private static object Role(string id, string role, Guid merchantId) =>
            new { id, name = role, path = $"/merchants/{merchantId:D}/{role}" };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var isGet = request.Method == HttpMethod.Get;
            if (path.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
                return Task.FromResult(Json(new { access_token = "service-token" }));

            ReceivedManagementRequest = true;
            if (isGet && path.EndsWith("/users", StringComparison.Ordinal))
                return Task.FromResult(UserLookup(request.RequestUri.Query));
            if (isGet && path.EndsWith("/groups", StringComparison.Ordinal) &&
                path.Contains("/users/", StringComparison.Ordinal))
                return Task.FromResult(Json(path.Contains($"/users/{member:D}/", StringComparison.Ordinal)
                    ? new[] { Role("operator-id", "OPERATOR", merchant) }
                    : new[] { Role("admin-id", "ADMIN", merchant) }));
            if (isGet && path.EndsWith("/groups", StringComparison.Ordinal))
                return Task.FromResult(Json(new[] { new { id = "root-id", name = "merchants", path = "/merchants" } }));
            if (isGet && path.EndsWith("/groups/root-id/children", StringComparison.Ordinal))
                return Task.FromResult(Json(new[] { new { id = "merchant-id", name = merchant.ToString("D"),
                    path = $"/merchants/{merchant:D}" } }));
            if (isGet && path.EndsWith("/groups/merchant-id/children", StringComparison.Ordinal))
                return Task.FromResult(Json(new[] { Role("admin-id", "ADMIN", merchant),
                    Role("operator-id", "OPERATOR", merchant), Role("inactive-id", "INACTIVE", merchant) }));
            if (request.Method == HttpMethod.Post && path.EndsWith("/users", StringComparison.Ordinal))
            {
                created = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
            }
            if (request.Method == HttpMethod.Put && path.EndsWith($"/users/{member:D}", StringComparison.Ordinal)
                && denyRename)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            if (request.Method is { } method && (method == HttpMethod.Put || method == HttpMethod.Delete))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private HttpResponseMessage UserLookup(string query)
        {
            if (query.Contains("new.name", StringComparison.Ordinal))
                return Json(Array.Empty<object>());
            if (query.Contains("new.operator", StringComparison.Ordinal) && !created)
                return Json(Array.Empty<object>());
            return Json(new[] { new { id = member.ToString("D"),
                username = query.Contains("new.operator", StringComparison.Ordinal)
                    ? "new.operator" : "old.operator" } });
        }
    }

    private static async Task<HttpResponseMessage> Submit(HttpClient client, string amount,
        bool includeIdempotency = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions")
        {
            Content = JsonContent.Create(new
            {
                merchant_id = Guid.NewGuid(), type = "DEBIT", amount,
                reference_date = "2020-01-01"
            })
        };
        if (includeIdempotency)
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        return await client.SendAsync(request);
    }

    private sealed class MockAuthorization : ICreateTransactionAuthorizationGateway
    {
        public bool Allowed { get; set; } = true;
        public bool Outage { get; set; }
        public Guid LastMerchant { get; private set; }
        public Task<bool> CanCreateAsync(Guid actorId, Guid merchantId, CancellationToken ct)
        {
            LastMerchant = merchantId;
            if (Outage)
                throw new CashFlow.Security.Keycloak.MerchantAuthorizationUnavailableException("Simulated outage");
            return Task.FromResult(Allowed);
        }
    }

    private sealed class MockRegistration : ITransactionRegistrationGateway
    {
        public int Calls { get; private set; }
        public bool Conflict { get; set; }
        public Task<CreateTransactionResult> RegisterAsync(Transaction transaction,
            string key, string correlation, CancellationToken ct)
        {
            Calls++;
            if (Conflict) throw new TransactionIdempotencyConflictException();
            return Task.FromResult(new CreateTransactionResult(
                transaction.Id, transaction.MerchantId, transaction.Type,
                transaction.Amount, transaction.ReferenceDate, transaction.CreatedAtUtc));
        }
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
