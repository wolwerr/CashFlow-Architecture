using System.Net;
using System.Text;
using CashFlow.Consolidation.Infrastructure.Gateways;
using CashFlow.Security.Keycloak;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace CashFlow.Consolidation.IntegrationTests;

public sealed class KeycloakMerchantAuthorizationTests
{
    private static KeycloakAuthorizationSettings Settings() => new()
    {
        BaseUrl = "http://keycloak:8080",
        Realm = "cashflow",
        ClientId = "test-client",
        ClientSecret = "test-client-secret"
    };

    private static KeycloakConsolidationAuthorizationGateway Create(
        Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        var http = new HttpClient(new FakeHandler(response)) { BaseAddress = new Uri("http://keycloak:8080/") };
        return new KeycloakConsolidationAuthorizationGateway(http,
            new MemoryCache(new MemoryCacheOptions()), Options.Create(Settings()));
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("OPERATOR")]
    public async Task CurrentMerchantMembershipGrantsCreate(string role)
    {
        var merchant = Guid.NewGuid();
        var count = 0;
        var authz = Create(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
                return Json("{\"access_token\":\"service-token\",\"expires_in\":300}");
            Assert.Equal("Bearer service-token", req.Headers.Authorization!.ToString());
            count++;
            return Json($@"[{{""path"":""/merchants/{merchant:D}/{role}""}}]");
        });
        Assert.True(await authz.CanReadAsync(Guid.NewGuid(), merchant, default));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task DifferentMerchantAndGlobalRoleNeverGrant()
    {
        var merchant = Guid.NewGuid();
        var other = Guid.NewGuid();
        var gateway = Create(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)
                ? Json("{\"access_token\":\"service-token\",\"expires_in\":300}")
                : Json($@"[{{""path"":""/merchants/{other:D}/ADMIN""}},{{""path"":""/ADMIN""}}]"));
        Assert.False(await gateway.CanReadAsync(Guid.NewGuid(), merchant, default));
    }

    [Fact]
    public async Task RevocationIsObservedOnNextCallWithoutPermissionCache()
    {
        var merchant = Guid.NewGuid();
        var checks = 0;
        var tokens = 0;
        var gateway = Create(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
            {
                tokens++;
                return Json("{\"access_token\":\"service-token\",\"expires_in\":300}");
            }
            return Json(++checks == 1
                ? $@"[{{""path"":""/merchants/{merchant:D}/OPERATOR""}}]"
                : "[]");
        });
        Assert.True(await gateway.CanReadAsync(Guid.NewGuid(), merchant, default));
        Assert.False(await gateway.CanReadAsync(Guid.NewGuid(), merchant, default));
        Assert.Equal(2, checks);
        Assert.Equal(1, tokens); // Service credential cache only, not permissions.
    }

    [Fact]
    public async Task KeycloakUnavailableFailsClosed()
    {
        var gateway = Create(req => req.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)
            ? Json("{\"access_token\":\"service-token\",\"expires_in\":300}")
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<MerchantAuthorizationUnavailableException>(() =>
            gateway.CanReadAsync(Guid.NewGuid(), Guid.NewGuid(), default));
    }

    [Fact]
    public async Task MissingUserDoesNotGrant()
    {
        var gateway = Create(req => req.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)
            ? Json("{\"access_token\":\"service-token\",\"expires_in\":300}")
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.False(await gateway.CanReadAsync(Guid.NewGuid(), Guid.NewGuid(), default));
    }

    [Fact]
    public async Task MissingServiceTokenFailsClosed()
    {
        var gateway = Create(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<MerchantAuthorizationUnavailableException>(() =>
            gateway.CanReadAsync(Guid.NewGuid(), Guid.NewGuid(), default));
    }

    [Fact]
    public async Task LookupPaginatesAllGroupsBeforeAuthorizing()
    {
        var merchant = Guid.NewGuid();
        var pages = 0;
        var gateway = Create(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
                return Json("{\"access_token\":\"service-token\",\"expires_in\":300}");
            pages++;
            if (pages == 1)
            {
                var groups = Enumerable.Range(0, 100)
                    .Select(n => $@"{{""path"":""/unrelated/{n}""}}");
                return Json("[" + string.Join(",", groups) + "]");
            }
            return Json($@"[{{""path"":""/merchants/{merchant:D}/ADMIN""}}]");
        });
        Assert.True(await gateway.CanReadAsync(Guid.NewGuid(), merchant, default));
        Assert.Equal(2, pages);
    }

    [Fact]
    public async Task BadKeycloakJsonFailsClosed()
    {
        var gateway = Create(req => req.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)
            ? Json("{\"access_token\":\"service-token\",\"expires_in\":300}")
            : Json("invalid-json"));
        await Assert.ThrowsAsync<MerchantAuthorizationUnavailableException>(() =>
            gateway.CanReadAsync(Guid.NewGuid(), Guid.NewGuid(), default));
    }

    [Fact]
    public async Task EmptyGroupListDeniesPermission()
    {
        var gateway = Create(req => req.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)
            ? Json("{\"access_token\":\"service-token\",\"expires_in\":300}")
            : Json("[]"));
        Assert.False(await gateway.CanReadAsync(Guid.NewGuid(), Guid.NewGuid(), default));
    }

    [Fact]
    public void KeycloakSettingsMustHaveServiceCredentials()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new KeycloakAuthorizationSettings { BaseUrl = "http://localhost:8081" }.Validate(true));
        Assert.Throws<InvalidOperationException>(() => Settings().Validate(false));
        Settings().Validate(true);
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }
}
