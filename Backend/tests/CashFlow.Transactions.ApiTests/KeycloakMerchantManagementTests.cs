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
                JsonResponse(new[] { Admin() }),
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
    public async Task MerchantDirectoryRejectsSimultaneousAdminAndOperatorRoles()
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
        Assert.Empty(await Create(fake).ListAsync(UserId, CancellationToken.None));
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
    public async Task AdminPermissionFailsClosedWhenInactiveAndAdminOverlap()
    {
        var fake = new FakeHandler(req => TokenOr(req, request =>
            Path(request).EndsWith($"/users/{UserId:D}/groups", StringComparison.Ordinal)
                ? JsonResponse(new object[] { Admin(), new {
                    id = "inactive-id", name = "INACTIVE",
                    path = $"/merchants/{MerchantId:D}/INACTIVE"
                } })
                : new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.False(await Create(fake).IsAdminAsync(UserId, MerchantId, CancellationToken.None));
    }

    [Fact]
    public async Task MerchantDirectoryDoesNotGrantAmbiguousAdminMembership()
    {
        var fake = new FakeHandler(req => TokenOr(req, request => Path(request) switch
        {
            var p when p.EndsWith($"/users/{UserId:D}/groups", StringComparison.Ordinal) =>
                JsonResponse(new object[] { Admin(), new {
                    id = "inactive-id", name = "INACTIVE",
                    path = $"/merchants/{MerchantId:D}/INACTIVE"
                } }),
            var p when p == "/admin/realms/cashflow/groups" => JsonResponse(new[] { Root() }),
            var p when p == "/admin/realms/cashflow/groups/root-id/children" =>
                JsonResponse(new[] { Merchant() }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        }));
        Assert.Empty(await Create(fake).ListAsync(UserId, CancellationToken.None));
    }

    [Fact]
    public async Task ListsActualMerchantUsersAndTheirRoleRatherThanTheRequestingAdminRole()
    {
        var fake = new FakeHandler(req => TokenOr(req, request =>
        {
            var path = Path(request);
            if (path.EndsWith("/groups", StringComparison.Ordinal)
                && request.Method == HttpMethod.Get) return JsonResponse(new[] { Root() });
            if (path.EndsWith("/groups/root-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Merchant() });
            if (path.EndsWith("/groups/merchant-group-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Admin(), Operator() });
            if (path.EndsWith("/groups/admin-id/members", StringComparison.Ordinal))
                return JsonResponse(new[] { new { id = UserId.ToString("D"), username = "cashflow.admin" } });
            if (path.EndsWith("/groups/operator-id/members", StringComparison.Ordinal))
                return JsonResponse(new[] { new { id = Guid.NewGuid().ToString("D"), username = "loja.teste" } });
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var list = await Create(fake).ListMembersAsync(MerchantId, CancellationToken.None);
        Assert.Contains(list, member => member.Username == "cashflow.admin" && member.Role == "ADMIN");
        Assert.Contains(list, member => member.Username == "loja.teste" && member.Role == "OPERATOR");
        Assert.Equal(2, list.Count);
        Assert.DoesNotContain(fake.Calls, call => call.Method == "POST" &&
            call.Path.Contains("members", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingMerchantDoesNotReturnMembersOrReadUnrelatedGroups()
    {
        var fake = new FakeHandler(req => TokenOr(req, request =>
            Path(request).EndsWith("/groups", StringComparison.Ordinal) &&
            request.Method == HttpMethod.Get
                ? JsonResponse(Array.Empty<object>())
                : new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.Empty(await Create(fake).ListMembersAsync(MerchantId, CancellationToken.None));
        Assert.DoesNotContain(fake.Calls, call => call.Path.Contains("/members", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsMemberDirectoryIfKeycloakUserLookupFails()
    {
        var fake = new FakeHandler(req => TokenOr(req, request =>
        {
            var path = Path(request);
            if (path == "/admin/realms/cashflow/groups") return JsonResponse(new[] { Root() });
            if (path.EndsWith("/groups/root-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Merchant() });
            if (path.EndsWith("/groups/merchant-group-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Admin(), Operator() });
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Create(fake).ListMembersAsync(MerchantId, CancellationToken.None));
    }

    [Fact]
    public async Task ConflictingRolesFailClosedBeforeAnyMembershipMutation()
    {
        var fake = new FakeHandler(req => TokenOr(req, request =>
        {
            var path = Path(request);
            if (path.EndsWith("/users", StringComparison.Ordinal))
                return JsonResponse(new[] { new { id = UserId.ToString("D"), username = "loja.teste" } });
            if (path == "/admin/realms/cashflow/groups") return JsonResponse(new[] { Root() });
            if (path.EndsWith("/groups/root-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Merchant() });
            if (path.EndsWith("/groups/merchant-group-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Admin(), Operator() });
            if (path.EndsWith($"/users/{UserId:D}/groups", StringComparison.Ordinal))
                return JsonResponse(new[] { Admin(), Operator() });
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Create(fake).ChangeMemberRoleAsync(MerchantId, "loja.teste", "OPERATOR",
                "cashflow.admin", CancellationToken.None));
        Assert.DoesNotContain(fake.Calls, call => call.Method is "PUT" or "DELETE");
    }

    [Fact]
    public async Task ConcurrentUsernameCreationConflictDoesNotLinkAnotherIdentity()
    {
        var reads = 0;
        var fake = new FakeHandler(req => TokenOr(req, request =>
        {
            var path = Path(request);
            if (path == "/admin/realms/cashflow/groups") return JsonResponse(new[] { Root() });
            if (path.EndsWith("/groups/root-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Merchant() });
            if (path.EndsWith("/groups/merchant-group-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Admin(), Operator() });
            if (path.EndsWith("/users", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            {
                reads++;
                return JsonResponse(Array.Empty<object>());
            }
            if (path.EndsWith("/users", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.Conflict);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        await Assert.ThrowsAsync<MerchantUsernameConflictException>(() =>
            Create(fake).AddOperatorAsync(MerchantId, "loja.teste", "senha-forte-teste", CancellationToken.None));
        Assert.True(reads >= 1);
        Assert.DoesNotContain(fake.Calls, call => call.Method == "PUT" &&
            call.Path.Contains("/users/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedGroupAssignmentDeletesOnlyTheUserCreatedInCurrentRequest()
    {
        var created = false;
        var fake = new FakeHandler(req => TokenOr(req, request =>
        {
            var path = Path(request);
            if (path == "/admin/realms/cashflow/groups") return JsonResponse(new[] { Root() });
            if (path.EndsWith("/groups/root-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Merchant() });
            if (path.EndsWith("/groups/merchant-group-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Admin(), Operator() });
            if (path.EndsWith("/users", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
                return created
                    ? JsonResponse(new[] { new { id = UserId.ToString("D"), username = "loja.teste" } })
                    : JsonResponse(Array.Empty<object>());
            if (path.EndsWith("/users", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                created = true;
                return new HttpResponseMessage(HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Put &&
                path.EndsWith($"/users/{UserId:D}/groups/operator-id", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (request.Method == HttpMethod.Delete &&
                path.EndsWith($"/users/{UserId:D}", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Create(fake).AddOperatorAsync(MerchantId, "loja.teste", "senha-forte-teste", CancellationToken.None));
        Assert.Contains(fake.Calls, call => call.Method == "DELETE" &&
            call.Path.EndsWith($"/users/{UserId:D}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedAssignmentNeverDeletesExistingRealmUser()
    {
        var fake = new FakeHandler(req => TokenOr(req, request =>
        {
            var path = Path(request);
            if (path.EndsWith("/users", StringComparison.Ordinal))
                return JsonResponse(new[] { new { id = UserId.ToString("D"), username = "manager" } });
            if (path == "/admin/realms/cashflow/groups") return JsonResponse(new[] { Root() });
            if (path.EndsWith("/groups/root-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Merchant() });
            if (path.EndsWith("/groups/merchant-group-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Admin(), Operator() });
            if (request.Method == HttpMethod.Put &&
                path.EndsWith($"/users/{UserId:D}/groups/admin-id", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Create(fake).AddAdminAsync(MerchantId, "manager", null, CancellationToken.None));
        Assert.DoesNotContain(fake.Calls, call => call.Method == "DELETE" &&
            call.Path.EndsWith($"/users/{UserId:D}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RenamingMemberPreservesIdentityAndMembership()
    {
        var changes = 0;
        var fake = new FakeHandler(req => TokenOr(req, request =>
        {
            var path = Path(request);
            if (request.Method == HttpMethod.Get &&
                path.EndsWith("/users", StringComparison.Ordinal))
                return JsonResponse(request.RequestUri!.Query.Contains("loja.nova", StringComparison.Ordinal)
                    ? Array.Empty<object>() : new[] { new { id = UserId.ToString("D"), username = "loja.teste" } });
            if (path == "/admin/realms/cashflow/groups") return JsonResponse(new[] { Root() });
            if (path.EndsWith("/groups/root-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Merchant() });
            if (path.EndsWith("/groups/merchant-group-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Admin(), Operator() });
            if (path.EndsWith($"/users/{UserId:D}/groups", StringComparison.Ordinal))
                return JsonResponse(new[] { Operator() });
            if (request.Method == HttpMethod.Put &&
                path.EndsWith($"/users/{UserId:D}", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result);
                Assert.Equal("loja.nova", body.RootElement.GetProperty("username").GetString());
                changes++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        Assert.True(await Create(fake).RenameMemberAsync(MerchantId, "loja.teste",
            "loja.nova", "cashflow.admin", CancellationToken.None));
        Assert.Equal(1, changes);
        Assert.DoesNotContain(fake.Calls, call => call.Method == "POST" &&
            call.Path.EndsWith("/users", StringComparison.Ordinal));
        Assert.DoesNotContain(fake.Calls, call => call.Method == "DELETE");
    }

    [Fact]
    public async Task RenameMemberRejectedByRealmProducesActionableConflict()
    {
        var fake = new FakeHandler(req => TokenOr(req, request =>
        {
            var path = Path(request);
            if (request.Method == HttpMethod.Get && path.EndsWith("/users", StringComparison.Ordinal))
                return JsonResponse(request.RequestUri!.Query.Contains("loja.nova", StringComparison.Ordinal)
                    ? Array.Empty<object>()
                    : new[] { new { id = UserId.ToString("D"), username = "loja.teste" } });
            if (path == "/admin/realms/cashflow/groups") return JsonResponse(new[] { Root() });
            if (path.EndsWith("/groups/root-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Merchant() });
            if (path.EndsWith("/groups/merchant-group-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Admin(), Operator() });
            if (path.EndsWith($"/users/{UserId:D}/groups", StringComparison.Ordinal))
                return JsonResponse(new[] { Operator() });
            if (request.Method == HttpMethod.Put &&
                path.EndsWith($"/users/{UserId:D}", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        await Assert.ThrowsAsync<MerchantUsernameChangeNotAllowedException>(() =>
            Create(fake).RenameMemberAsync(MerchantId, "loja.teste", "loja.nova",
                "cashflow.admin", CancellationToken.None));
        Assert.DoesNotContain(fake.Calls, call => call.Method == "DELETE");
    }

    [Fact]
    public async Task RenamingMemberToExistingRealmUsernameIsRejected()
    {
        var fake = new FakeHandler(req => TokenOr(req, request =>
        {
            var path = Path(request);
            if (request.Method == HttpMethod.Get && path.EndsWith("/users", StringComparison.Ordinal))
                return JsonResponse(request.RequestUri!.Query.Contains("loja.nova", StringComparison.Ordinal)
                    ? new[] { new { id = Guid.NewGuid().ToString("D"), username = "loja.nova" } }
                    : new[] { new { id = UserId.ToString("D"), username = "loja.teste" } });
            if (path == "/admin/realms/cashflow/groups") return JsonResponse(new[] { Root() });
            if (path.EndsWith("/groups/root-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Merchant() });
            if (path.EndsWith("/groups/merchant-group-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Admin(), Operator() });
            if (path.EndsWith($"/users/{UserId:D}/groups", StringComparison.Ordinal))
                return JsonResponse(new[] { Operator() });
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        await Assert.ThrowsAsync<MerchantUsernameConflictException>(() =>
            Create(fake).RenameMemberAsync(MerchantId, "loja.teste", "loja.nova",
                "cashflow.admin", CancellationToken.None));
        Assert.DoesNotContain(fake.Calls, call => call.Method == "PUT");
    }

    [Fact]
    public async Task CannotRenameUserSharedWithAnotherMerchant()
    {
        var otherMerchant = Guid.NewGuid();
        var fake = new FakeHandler(req => TokenOr(req, request =>
        {
            var path = Path(request);
            if (request.Method == HttpMethod.Get && path.EndsWith("/users", StringComparison.Ordinal))
                return JsonResponse(new[] { new { id = UserId.ToString("D"), username = "loja.teste" } });
            if (path == "/admin/realms/cashflow/groups") return JsonResponse(new[] { Root() });
            if (path.EndsWith("/groups/root-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Merchant() });
            if (path.EndsWith("/groups/merchant-group-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Admin(), Operator() });
            if (path.EndsWith($"/users/{UserId:D}/groups", StringComparison.Ordinal))
                return JsonResponse(new object[] { Operator(), new {
                    id = "other-id", name = "OPERATOR",
                    path = $"/merchants/{otherMerchant:D}/OPERATOR"
                } });
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        await Assert.ThrowsAsync<MerchantProtectedAccountException>(() =>
            Create(fake).RenameMemberAsync(MerchantId, "loja.teste", "loja.nova",
                "cashflow.admin", CancellationToken.None));
        Assert.DoesNotContain(fake.Calls, call => call.Method == "PUT");
    }

    [Fact]
    public async Task BootstrapUsernameCannotBeEditedOrReassigned()
    {
        var fake = new FakeHandler(_ => throw new InvalidOperationException("No Keycloak calls expected"));
        await Assert.ThrowsAsync<MerchantProtectedAccountException>(() =>
            Create(fake).RenameMemberAsync(MerchantId, "cashflow.admin", "new.admin",
                "cashflow.admin", CancellationToken.None));
        await Assert.ThrowsAsync<MerchantProtectedAccountException>(() =>
            Create(fake).RenameMemberAsync(MerchantId, "loja.teste", "cashflow.admin",
                "cashflow.admin", CancellationToken.None));
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task BootstrapAdministratorCannotBeRemovedOrDemoted()
    {
        var fake = new FakeHandler(_ => throw new InvalidOperationException("Should not call Keycloak."));
        var service = Create(fake);
        await Assert.ThrowsAsync<MerchantProtectedAccountException>(() =>
            service.DeactivateMemberAsync(MerchantId, "cashflow.admin", "cashflow.admin", CancellationToken.None));
        await Assert.ThrowsAsync<MerchantProtectedAccountException>(() =>
            service.ChangeMemberRoleAsync(MerchantId, "cashflow.admin", "OPERATOR",
                "cashflow.admin", CancellationToken.None));
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task RenamingMerchantPreservesExistingAttributesAndUuid()
    {
        var fake = new FakeHandler(req => TokenOr(req, request =>
        {
            var path = Path(request);
            if (path == "/admin/realms/cashflow/groups") return JsonResponse(new[] { Root() });
            if (path.EndsWith("/groups/root-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Merchant() });
            if (path.EndsWith("/groups/merchant-group-id", StringComparison.Ordinal) &&
                request.Method == HttpMethod.Put)
            {
                using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result);
                Assert.Equal(MerchantId.ToString("D"), body.RootElement.GetProperty("name").GetString());
                Assert.Equal("Loja Nova", body.RootElement.GetProperty("attributes")
                    .GetProperty("displayName")[0].GetString());
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        Assert.True(await Create(fake).RenameMerchantAsync(MerchantId, "Loja Nova", CancellationToken.None));
    }

    [Fact]
    public async Task RemovingOperatorOnlyRevokesMerchantGroupMembership()
    {
        var fake = new FakeHandler(req => TokenOr(req, request =>
        {
            var path = Path(request);
            if (path.EndsWith("/users", StringComparison.Ordinal))
                return JsonResponse(new[] { new { id = UserId.ToString("D"), username = "loja.teste" } });
            if (path == "/admin/realms/cashflow/groups") return JsonResponse(new[] { Root() });
            if (path.EndsWith("/groups/root-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Merchant() });
            if (path.EndsWith("/groups/merchant-group-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Admin(), Operator(), new {
                    id = "inactive-id", name = "INACTIVE", path = $"/merchants/{MerchantId:D}/INACTIVE" } });
            if (path.EndsWith($"/users/{UserId:D}/groups", StringComparison.Ordinal))
                return JsonResponse(new[] { Operator() });
            if (request.Method == HttpMethod.Put && path.EndsWith($"/users/{UserId:D}/groups/inactive-id",
                StringComparison.Ordinal)) return new HttpResponseMessage(HttpStatusCode.NoContent);
            if (request.Method == HttpMethod.Delete &&
                path.EndsWith($"/users/{UserId:D}/groups/operator-id", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        Assert.True(await Create(fake).DeactivateMemberAsync(MerchantId,
            "loja.teste", "cashflow.admin", CancellationToken.None));
        Assert.Contains(fake.Calls, c => c.Method == "DELETE" && c.Path.Contains("operator-id"));
        Assert.DoesNotContain(fake.Calls, c => c.Method == "DELETE" &&
            c.Path.EndsWith($"/users/{UserId:D}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SoftDeletedOperatorCanBeReactivatedWithoutCreatingAnotherUser()
    {
        var fake = new FakeHandler(req => TokenOr(req, request =>
        {
            var path = Path(request);
            if (path.EndsWith("/users", StringComparison.Ordinal))
                return JsonResponse(new[] { new { id = UserId.ToString("D"), username = "loja.teste" } });
            if (path == "/admin/realms/cashflow/groups") return JsonResponse(new[] { Root() });
            if (path.EndsWith("/groups/root-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Merchant() });
            if (path.EndsWith("/groups/merchant-group-id/children", StringComparison.Ordinal))
                return JsonResponse(new object[] { Admin(), Operator(), new {
                    id = "inactive-id", name = "INACTIVE", path = $"/merchants/{MerchantId:D}/INACTIVE" } });
            if (path.EndsWith($"/users/{UserId:D}/groups", StringComparison.Ordinal))
                return JsonResponse(new[] { new {
                    id = "inactive-id", name = "INACTIVE", path = $"/merchants/{MerchantId:D}/INACTIVE" } });
            if (path.Contains($"/users/{UserId:D}/groups/", StringComparison.Ordinal) &&
                (request.Method == HttpMethod.Delete || request.Method == HttpMethod.Put))
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        Assert.True(await Create(fake).ChangeMemberRoleAsync(
            MerchantId, "loja.teste", "OPERATOR", "cashflow.admin", CancellationToken.None));
        var changes = fake.Calls.Where(c => c.Path.Contains($"/users/{UserId:D}/groups/")).ToList();
        Assert.Equal("PUT", changes[0].Method);
        Assert.EndsWith("/operator-id", changes[0].Path);
        Assert.Equal("DELETE", changes[1].Method);
        Assert.EndsWith("/inactive-id", changes[1].Path);
        Assert.DoesNotContain(fake.Calls, call => call.Method == "POST" &&
            call.Path.EndsWith("/users", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateRoleAssignsNewRoleBeforeRemovingOldRole()
    {
        var fake = new FakeHandler(req => TokenOr(req, request =>
        {
            var path = Path(request);
            if (path.EndsWith("/users", StringComparison.Ordinal))
                return JsonResponse(new[] { new { id = UserId.ToString("D"), username = "loja.teste" } });
            if (path == "/admin/realms/cashflow/groups") return JsonResponse(new[] { Root() });
            if (path.EndsWith("/groups/root-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Merchant() });
            if (path.EndsWith("/groups/merchant-group-id/children", StringComparison.Ordinal))
                return JsonResponse(new[] { Admin(), Operator() });
            if (path.EndsWith($"/users/{UserId:D}/groups", StringComparison.Ordinal))
                return JsonResponse(new[] { Operator() });
            if (path.EndsWith($"/users/{UserId:D}/groups/admin-id", StringComparison.Ordinal) ||
                path.EndsWith($"/users/{UserId:D}/groups/operator-id", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        Assert.True(await Create(fake).ChangeMemberRoleAsync(
            MerchantId, "loja.teste", "ADMIN", "cashflow.admin", CancellationToken.None));
        var changes = fake.Calls.Where(c => c.Path.Contains($"/users/{UserId:D}/groups/")).ToList();
        Assert.Equal("PUT", changes[0].Method);
        Assert.EndsWith("/admin-id", changes[0].Path);
        Assert.Equal("DELETE", changes[1].Method);
        Assert.EndsWith("/operator-id", changes[1].Path);
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
    public async Task DuplicateMerchantUsernameDoesNotCreateAnyGroups()
    {
        var fake = new FakeHandler(req => TokenOr(req, request =>
            Path(request).EndsWith("/users", StringComparison.Ordinal)
                ? JsonResponse(new[] { new { id = UserId.ToString("D"), username = "loja.campinas" } })
                : new HttpResponseMessage(HttpStatusCode.NotFound)));
        await Assert.ThrowsAsync<MerchantUsernameConflictException>(() =>
            Create(fake).CreateWithOperatorAsync("Loja Campinas", UserId,
                "loja.campinas", "senha-local-segura", CancellationToken.None));
        Assert.DoesNotContain(fake.Calls, call => call.Method == "POST" &&
            call.Path.EndsWith("/groups", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NewMerchantAccountIsOperatorNotAdmin()
    {
        var rootCreated = false;
        string? generatedId = null;
        var merchantCreated = false;
        var adminCreated = false;
        var operatorCreated = false;
        var inactiveCreated = false;
        var userCreated = false;
        var assignedAdmin = false;
        var assignedOperator = false;
        var fake = new FakeHandler(req => TokenOr(req, request =>
        {
            var path = Path(request);
            var method = request.Method;
            if (path.EndsWith("/users", StringComparison.Ordinal) && method == HttpMethod.Get)
                return userCreated ? JsonResponse(new[] {
                    new { id = "788cad48-77e3-4e09-b6b1-2d39c58b2233", username = "loja.campinas" }
                }) : JsonResponse(Array.Empty<object>());
            if (path.EndsWith("/users", StringComparison.Ordinal) && method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result);
                Assert.Equal("loja.campinas", body.RootElement.GetProperty("username").GetString());
                var credential = body.RootElement.GetProperty("credentials")[0];
                Assert.False(credential.GetProperty("temporary").GetBoolean());
                Assert.Equal("senha-local-segura", credential.GetProperty("value").GetString());
                userCreated = true;
                return new HttpResponseMessage(HttpStatusCode.Created);
            }
            if (path.EndsWith("/groups", StringComparison.Ordinal) && method == HttpMethod.Get)
                return JsonResponse(rootCreated ? new[] { Root() } : Array.Empty<object>());
            if (path.EndsWith("/groups", StringComparison.Ordinal) && method == HttpMethod.Post)
            {
                rootCreated = true;
                return new HttpResponseMessage(HttpStatusCode.Created);
            }
            if (path.EndsWith("/groups/root-id/children", StringComparison.Ordinal) && method == HttpMethod.Get)
                return JsonResponse(merchantCreated ? new[] {
                    new { id = "merchant-group-id", name = generatedId, path = "/merchants/" + generatedId }
                } : Array.Empty<object>());
            if (path.EndsWith("/groups/root-id/children", StringComparison.Ordinal) && method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result);
                generatedId = body.RootElement.GetProperty("name").GetString();
                merchantCreated = true;
                return new HttpResponseMessage(HttpStatusCode.Created);
            }
            if (path.EndsWith("/groups/merchant-group-id/children", StringComparison.Ordinal) && method == HttpMethod.Get)
            {
                var roles = new List<object>();
                if (adminCreated) roles.Add(new {
                    id = "admin-id", name = "ADMIN", path = "/merchants/" + generatedId + "/ADMIN" });
                if (operatorCreated) roles.Add(new {
                    id = "operator-id", name = "OPERATOR", path = "/merchants/" + generatedId + "/OPERATOR" });
                if (inactiveCreated) roles.Add(new {
                    id = "inactive-id", name = "INACTIVE", path = "/merchants/" + generatedId + "/INACTIVE" });
                return JsonResponse(roles);
            }
            if (path.EndsWith("/groups/merchant-group-id/children", StringComparison.Ordinal) && method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result);
                var role = body.RootElement.GetProperty("name").GetString();
                if (role == "ADMIN") adminCreated = true;
                else if (role == "OPERATOR") operatorCreated = true;
                else if (role == "INACTIVE") inactiveCreated = true;
                return new HttpResponseMessage(HttpStatusCode.Created);
            }
            if (method == HttpMethod.Put && path.EndsWith("/groups/admin-id", StringComparison.Ordinal))
            {
                assignedAdmin = true;
                Assert.Contains(UserId.ToString("D"), path, StringComparison.Ordinal);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (method == HttpMethod.Put && path.EndsWith("/groups/operator-id", StringComparison.Ordinal))
            {
                assignedOperator = true;
                Assert.DoesNotContain(UserId.ToString("D"), path, StringComparison.Ordinal);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (method == HttpMethod.Put) return new HttpResponseMessage(HttpStatusCode.NoContent);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var result = await Create(fake).CreateWithOperatorAsync(
            "Loja Campinas", UserId, "loja.campinas", "senha-local-segura", CancellationToken.None);
        Assert.Equal(generatedId, result.Id.ToString("D"));
        Assert.True(assignedAdmin);
        Assert.True(assignedOperator);
        Assert.True(userCreated);
        Assert.Contains(fake.Calls, call => call.Path.EndsWith("/groups/operator-id", StringComparison.Ordinal));
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
    public async Task CreatesAnAdministratorWithTemporaryPasswordWhenUserDoesNotExist()
    {
        var userCreated = false;
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
                return userCreated ? JsonResponse(new[] {
                    new { id = UserId.ToString("D"), username = "new-manager" }
                }) : JsonResponse(Array.Empty<object>());
            if (path.EndsWith("/users", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                userCreated = true;
                return new HttpResponseMessage(HttpStatusCode.Created);
            }
            if (path.EndsWith($"/users/{UserId:D}/groups/admin-id", StringComparison.Ordinal)
                && request.Method == HttpMethod.Put)
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var service = Create(fake);
        Assert.True(await service.AddAdminAsync(MerchantId,
            "new-manager", "a-temporary-password", CancellationToken.None));
        Assert.True(userCreated);
        Assert.Contains(fake.Calls, call => call.Method == "POST" &&
            call.Path.EndsWith("/users", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingUserReturnsFalseEvenWhenMerchantExists()
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
                return JsonResponse(Array.Empty<object>());
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        Assert.False(await Create(fake).AddAdminAsync(MerchantId,
            "unknown", null, CancellationToken.None));
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
