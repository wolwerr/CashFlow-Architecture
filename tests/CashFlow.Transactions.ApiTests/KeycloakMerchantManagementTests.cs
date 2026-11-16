using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CashFlow.Transactions.Api.Merchants;
using Microsoft.Extensions.Options;
using Xunit;

namespace CashFlow.Transactions.ApiTests;

public sealed class KeycloakMerchantManagementTests
{
    private static readonly Guid MerchantId = Guid.Parse("68a5baac-d97f-45b4-9119-b4c5b9de7c9b");
    private static readonly Guid UserId = Guid.Parse("86b883e9-487a-422e-a042-56fd351fc679");

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public List<(string Method, string Path)> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery));
            return Task.FromResult(responder(request));
        }
    }

    private static HttpResponseMessage JsonResponse(object value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(value) };

    private static KeycloakMerchantManagement Create(FakeHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://keycloak:8080/") };
        return new KeycloakMerchantManagement(http, Options.Create(new KeycloakManagementSettings
        {
            Realm = "cashflow", ClientId = "management", ClientSecret = "test-only"
        }));
    }

    private static string Path(HttpRequestMessage request) => request.RequestUri!.AbsolutePath;
    private static bool IsToken(HttpRequestMessage request) =>
        Path(request).EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal);

    private static HttpResponseMessage TokenOr(HttpRequestMessage request,
        Func<HttpRequestMessage, HttpResponseMessage> next) =>
        IsToken(request) ? JsonResponse(new { access_token = "token" }) : next(request);

    private static object Root() => new { id = "root-id", name = "merchants", path = "/merchants" };
    private static object Merchant() => new {
        id = "merchant-group-id", name = MerchantId.ToString("D"),
        path = $"/merchants/{MerchantId:D}",
        attributes = new Dictionary<string, string[]> { ["displayName"] = ["Loja Campinas"] }
    };
    private static object Admin() => new {
        id = "admin-id", name = "ADMIN",
        path = $"/merchants/{MerchantId:D}/ADMIN"
    };
    private static object Operator() => new {
        id = "operator-id", name = "OPERATOR",
        path = $"/merchants/{MerchantId:D}/OPERATOR"
    };

    [Fact]
    public async Task ListsOnlyMerchantsOfCurrentUserWithCurrentRoleAndDisplayName()
    {
        var fake = new FakeHandler(req => TokenOr(req, request => Path(request) switch
        {
            var p when p.EndsWith($"/users/{UserId:D}/groups", StringComparison.Ordinal) =>
                JsonResponse(new[] { Admin(), Operator() }),
            var p when p == "/admin/realms/cashflow/groups" => JsonResponse(new[] { Root() }),
            var p when p == "/admin/realms/cashflow/groups/root-id/children" =>
                JsonResponse(new[] { Merchant() }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        }));
        var service = Create(fake);
        var list = await service.ListAsync(UserId, CancellationToken.None);
        var only = Assert.Single(list);
        Assert.Equal(MerchantId, only.Id);
        Assert.Equal("Loja Campinas", only.Name);
        Assert.Equal("ADMIN", only.Role);
    }

    [Fact]
    public async Task IgnoresUnrelatedGroupsAndMissingMerchant()
    {
        var fake = new FakeHandler(req => TokenOr(req, request =>
        {
            var path = Path(request);
            if (path.EndsWith($"/users/{UserId:D}/groups", StringComparison.Ordinal))
                return JsonResponse(new[] {
                    new { path = "/other/ADMIN", name = "ADMIN", id = "x" },
                    new { path = "/merchants/not-a-uuid/ADMIN", name = "ADMIN", id = "y" },
                    new { path = $"/merchants/{MerchantId:D}/ADMIN", name = "ADMIN", id = "z" }
                });
            return JsonResponse(Array.Empty<object>());
        }));
        Assert.Empty(await Create(fake).ListAsync(UserId, CancellationToken.None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdminPermissionRequiresExactMerchantGroupPath(bool member)
    {
        var fake = new FakeHandler(req => TokenOr(req, request =>
            Path(request).EndsWith($"/users/{UserId:D}/groups", StringComparison.Ordinal)
                ? JsonResponse(member ? new[] { Admin() } : new[] { Operator() })
                : new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.Equal(member, await Create(fake).IsAdminAsync(
            UserId, MerchantId, CancellationToken.None));
    }

    [Fact]
    public async Task CreatesMerchantHierarchyAssignsOwnerAndStoresDisplayName()
    {
        string? createdMerchantId = null;
        var groups = new Dictionary<string, List<object>>
        {
            ["groups"] = [], ["groups/root-id/children"] = [],
            ["groups/merchant-group-id/children"] = []
        };
        var fake = new FakeHandler(req => TokenOr(req, request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (request.Method == HttpMethod.Get && path.Contains("groups?search=merchants"))
                return JsonResponse(groups["groups"]);
            if (request.Method == HttpMethod.Get && path.Contains("groups/root-id/children"))
                return JsonResponse(groups["groups/root-id/children"]);
            if (request.Method == HttpMethod.Get && path.Contains("groups/merchant-group-id/children"))
                return JsonResponse(groups["groups/merchant-group-id/children"]);
            if (request.Method == HttpMethod.Post && path == "/admin/realms/cashflow/groups")
            {
                groups["groups"].Add(Root());
                return new HttpResponseMessage(HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("groups/root-id/children"))
            {
                // Group UUID is generated inside the application; fake its Keycloak group ID.
                using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result);
                var name = payload.RootElement.GetProperty("name").GetString()!;
                createdMerchantId = name;
                groups["groups/root-id/children"].Add(new {
                    id = "merchant-group-id", name, path = "/merchants/" + name
                });
                return new HttpResponseMessage(HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("groups/merchant-group-id/children"))
            {
                using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result);
                var name = payload.RootElement.GetProperty("name").GetString()!;
                groups["groups/merchant-group-id/children"].Add(new {
                    id = name == "ADMIN" ? "admin-id" : "operator-id",
                    name, path = $"/merchants/{createdMerchantId}/{name}"
                });
                return new HttpResponseMessage(HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Put) return new HttpResponseMessage(HttpStatusCode.NoContent);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var result = await Create(fake).CreateAsync("Loja Campinas", UserId, CancellationToken.None);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(result.Id.ToString("D"), createdMerchantId);
        Assert.Equal("Loja Campinas", result.Name);
        Assert.Equal("ADMIN", result.Role);
        Assert.Contains(fake.Calls, call => call.Method == "PUT" &&
            call.Path.EndsWith($"/users/{UserId:D}/groups/admin-id", StringComparison.Ordinal));
        Assert.Contains(fake.Calls, call => call.Method == "PUT" &&
            call.Path.EndsWith("/groups/merchant-group-id", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddsExistingUserToMerchantAdminGroup()
    {
        var fake = new FakeHandler(req => TokenOr(req, request =>
        {
            var path = Path(request);
            if (path.EndsWith("/groups", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
                return JsonResponse(new[] { Root() });
            if (path.EndsWith("/groups/root-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Merchant() });
            if (path.EndsWith("/groups/merchant-group-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Admin() });
            if (path.EndsWith("/users", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
                return JsonResponse(new[] { new { id = UserId.ToString("D"), username = "manager" } });
            if (path.EndsWith($"/users/{UserId:D}/groups/admin-id", StringComparison.Ordinal)
                && request.Method == HttpMethod.Put)
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var service = Create(fake);
        Assert.True(await service.AddAdminAsync(MerchantId, "manager", null, CancellationToken.None));
        Assert.Contains(fake.Calls, x => x.Path.EndsWith($"/users/{UserId:D}/groups/admin-id"));
    }

    [Fact]
    public async Task DeniesMissingMerchantAndMissingUserWithoutCreatingAccount()
    {
        var fake = new FakeHandler(req => TokenOr(req, request =>
            Path(request).EndsWith("/groups", StringComparison.Ordinal)
                ? JsonResponse(Array.Empty<object>()) : new HttpResponseMessage(HttpStatusCode.NotFound)));
        var service = Create(fake);
        Assert.False(await service.AddAdminAsync(MerchantId, "unknown", null, CancellationToken.None));
        Assert.DoesNotContain(fake.Calls, call => call.Method == "POST" &&
            call.Path.EndsWith("/users", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefusesManagementWhenServiceTokenIsUnavailable()
    {
        var fake = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Create(fake).ListAsync(UserId, CancellationToken.None));
    }

    [Fact]
    public async Task RefusesManagementWhenKeycloakGroupLookupFails()
    {
        var fake = new FakeHandler(req => IsToken(req)
            ? JsonResponse(new { access_token = "token" })
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Create(fake).IsAdminAsync(UserId, MerchantId, CancellationToken.None));
    }
}
