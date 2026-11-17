using System.Net;
using System.Net.Http.Json;
using CashFlow.Transactions.Api.Merchants;
using Microsoft.Extensions.Options;
using Xunit;

namespace CashFlow.Transactions.ApiTests;

public sealed class KeycloakMerchantManagementEdgeTests
{
    private static readonly Guid MerchantId = Guid.Parse("68a5baac-d97f-45b4-9119-b4c5b9de7c9b");
    private static readonly Guid UserId = Guid.Parse("86b883e9-487a-422e-a042-56fd351fc679");

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Method, string Path)> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method.Method, request.RequestUri!.AbsolutePath));
            if (request.RequestUri.AbsolutePath.EndsWith("/protocol/openid-connect/token",
                StringComparison.Ordinal))
                return Task.FromResult(Json(new { access_token = "test-token" }));
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    private static HttpResponseMessage NoContent() => new(HttpStatusCode.NoContent);
    private static string Path(HttpRequestMessage request) => request.RequestUri!.AbsolutePath;
    private static object Root() => new { id = "root-id", name = "merchants", path = "/merchants" };
    private static object Merchant() => new
    {
        id = "merchant-id", name = MerchantId.ToString("D"), path = $"/merchants/{MerchantId:D}"
    };
    private static object Role(string name) => new
    {
        id = name.ToLowerInvariant() + "-id", name,
        path = $"/merchants/{MerchantId:D}/{name}"
    };

    private static KeycloakMerchantManagement Service(FakeHandler fake) =>
        new(new HttpClient(fake) { BaseAddress = new Uri("http://keycloak:8080/") },
            Options.Create(new KeycloakManagementSettings
            {
                Realm = "cashflow", ClientId = "management", ClientSecret = "unit-test-only"
            }));

    private static FakeHandler WithMember(string role, Func<HttpRequestMessage, HttpResponseMessage>? mutation = null)
    {
        return new FakeHandler(request =>
        {
            var path = Path(request);
            if (request.Method == HttpMethod.Get && path.EndsWith("/users",
                StringComparison.Ordinal))
                return Json(new[] { new { id = UserId.ToString("D"), username = "loja.teste" } });
            if (request.Method == HttpMethod.Get &&
                path.EndsWith($"/users/{UserId:D}/groups", StringComparison.Ordinal))
                return Json(new[] { Role(role) });
            if (request.Method == HttpMethod.Get && path.EndsWith("/groups",
                StringComparison.Ordinal))
                return Json(new[] { Root() });
            if (request.Method == HttpMethod.Get && path.EndsWith("/groups/root-id/children",
                StringComparison.Ordinal))
                return Json(new[] { Merchant() });
            if (request.Method == HttpMethod.Get && path.EndsWith("/groups/merchant-id/children",
                StringComparison.Ordinal))
                return Json(new[] { Role("ADMIN"), Role("OPERATOR"), Role("INACTIVE") });
            return mutation?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        });
    }

    [Fact]
    public async Task RenameMissingMerchantDoesNotWriteToKeycloak()
    {
        var fake = new FakeHandler(request =>
            request.Method == HttpMethod.Get && Path(request).EndsWith("/groups",
                StringComparison.Ordinal)
                ? Json(Array.Empty<object>())
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.False(await Service(fake).RenameMerchantAsync(
            MerchantId, "Nome novo", CancellationToken.None));
        Assert.DoesNotContain(fake.Calls, x => x.Method == "PUT");
    }

    [Fact]
    public async Task ChangeRoleForUnknownUserDoesNotCreateAnAccount()
    {
        var fake = new FakeHandler(request =>
            request.Method == HttpMethod.Get && Path(request).EndsWith("/users",
                StringComparison.Ordinal)
                ? Json(Array.Empty<object>())
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.False(await Service(fake).ChangeMemberRoleAsync(
            MerchantId, "inexistente", "ADMIN", "cashflow.admin", CancellationToken.None));
        Assert.DoesNotContain(fake.Calls, x => x.Method is "PUT" or "DELETE");
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("OPERATOR")]
    public async Task ChangingToExistingRoleIsIdempotent(string role)
    {
        var fake = WithMember(role);
        Assert.True(await Service(fake).ChangeMemberRoleAsync(
            MerchantId, "loja.teste", role, "cashflow.admin", CancellationToken.None));
        Assert.DoesNotContain(fake.Calls, x => x.Method is "PUT" or "DELETE");
    }

    [Fact]
    public async Task DeactivatingAlreadyInactiveAccountIsIdempotent()
    {
        var fake = WithMember("INACTIVE");
        Assert.True(await Service(fake).DeactivateMemberAsync(
            MerchantId, "loja.teste", "cashflow.admin", CancellationToken.None));
        Assert.DoesNotContain(fake.Calls, x => x.Method is "PUT" or "DELETE");
    }

    [Fact]
    public async Task RenamingUserToSameUsernameDoesNotWriteToKeycloak()
    {
        var fake = WithMember("OPERATOR");
        Assert.True(await Service(fake).RenameMemberAsync(
            MerchantId, "loja.teste", "loja.teste", "cashflow.admin", CancellationToken.None));
        Assert.DoesNotContain(fake.Calls, call => call.Method is "PUT" or "DELETE");
    }

    [Fact]
    public async Task RenameUserRejectsKeycloakConflictEvenAfterAvailabilityCheck()
    {
        var fake = WithMember("OPERATOR", request =>
            request.Method == HttpMethod.Put &&
            Path(request).EndsWith($"/users/{UserId:D}", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.Conflict)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        await Assert.ThrowsAsync<MerchantUsernameConflictException>(() =>
            Service(fake).RenameMemberAsync(MerchantId, "loja.teste", "loja.nova",
                "cashflow.admin", CancellationToken.None));
    }

    [Fact]
    public async Task RenameUserRequiresCurrentMerchantMembership()
    {
        var fake = new FakeHandler(request =>
            request.Method == HttpMethod.Get && Path(request).EndsWith("/users",
                StringComparison.Ordinal)
                ? Json(Array.Empty<object>())
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.False(await Service(fake).RenameMemberAsync(
            MerchantId, "missing", "new.name", "cashflow.admin", CancellationToken.None));
        Assert.DoesNotContain(fake.Calls, call => call.Method is "PUT" or "DELETE");
    }

    [Fact]
    public async Task ChangingRoleRollsBackNewMembershipIfOldGroupRemovalFails()
    {
        var emptyMembership = new FakeHandler(request =>
        {
            var path = Path(request);
            if (request.Method != HttpMethod.Get) return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (path.EndsWith("/users", StringComparison.Ordinal))
                return Json(new[] { new { id = UserId.ToString("D"), username = "loja.teste" } });
            if (path.EndsWith($"/users/{UserId:D}/groups", StringComparison.Ordinal))
                return Json(Array.Empty<object>());
            if (path.EndsWith("/groups", StringComparison.Ordinal)) return Json(new[] { Root() });
            if (path.EndsWith("/groups/root-id/children", StringComparison.Ordinal)) return Json(new[] { Merchant() });
            if (path.EndsWith("/groups/merchant-id/children", StringComparison.Ordinal))
                return Json(new[] { Role("OPERATOR") });
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        Assert.False(await Service(emptyMembership).DeactivateMemberAsync(
            MerchantId, "loja.teste", "cashflow.admin", CancellationToken.None));
        Assert.DoesNotContain(emptyMembership.Calls, x => x.Method is "PUT" or "DELETE");
    }
}
