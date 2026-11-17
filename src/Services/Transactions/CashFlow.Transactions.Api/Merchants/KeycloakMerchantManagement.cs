using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace CashFlow.Transactions.Api.Merchants;

public sealed class KeycloakManagementSettings
{
    public const string SectionName = "KeycloakManagement";
    public string BaseUrl { get; set; } = "";
    public string Realm { get; set; } = "cashflow";
    public string ClientId { get; set; } = "cashflow-management-service";
    public string ClientSecret { get; set; } = "";
    public string BootstrapUsername { get; set; } = "";
}

public sealed class KeycloakMerchantManagement(
    HttpClient http, IOptions<KeycloakManagementSettings> options)
{
    private readonly KeycloakManagementSettings settings = options.Value;
    private const string RootName = "merchants";
    private const string AdminRole = "ADMIN";
    private const string OperatorRole = "OPERATOR";
    private const string InactiveRole = "INACTIVE";

    private sealed record GroupView(string Id, string Name, string? Path, Dictionary<string, string[]>? Attributes);
    private sealed record UserView(string Id, string Username);
    private sealed record ServiceToken([property: JsonPropertyName("access_token")] string AccessToken);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path,
        object? body, CancellationToken ct)
    {
        using var tokenResponse = await http.PostAsync(
            $"realms/{Uri.EscapeDataString(settings.Realm)}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret
            }), ct);
        if (!tokenResponse.IsSuccessStatusCode)
            throw new HttpRequestException("Keycloak management credentials unavailable.");
        var token = await tokenResponse.Content.ReadFromJsonAsync<ServiceToken>(cancellationToken: ct)
            ?? throw new HttpRequestException("Keycloak management token missing.");
        using var request = new HttpRequestMessage(method, $"admin/realms/{settings.Realm}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await http.SendAsync(request, ct);
    }

    private async Task<List<GroupView>> GroupsAsync(string path, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<GroupView>>(cancellationToken: ct) ?? [];
    }

    private async Task<GroupView?> FindRootAsync(CancellationToken ct)
    {
        var groups = await GroupsAsync("groups?search=merchants&exact=true&max=100", ct);
        return groups.FirstOrDefault(g => g.Name == RootName && g.Path == "/merchants");
    }

    private async Task<GroupView> GetOrCreateGroupAsync(GroupView? parent,
        string name, CancellationToken ct)
    {
        var listPath = parent is null ? "groups?search=merchants&exact=true&max=100"
            : $"groups/{parent.Id}/children?max=100";
        var expectedPath = parent is null ? $"/{name}" : $"{parent.Path}/{name}";
        var existing = (await GroupsAsync(listPath, ct))
            .FirstOrDefault(g => g.Name == name && g.Path == expectedPath);
        if (existing is not null) return existing;
        var createPath = parent is null ? "groups" : $"groups/{parent.Id}/children";
        using var response = await SendAsync(HttpMethod.Post, createPath,
            new { name }, ct);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // A competing request may have created the exact same group.
            // Accept only the group read back at its expected hierarchical path.
            var winner = (await GroupsAsync(listPath, ct))
                .FirstOrDefault(g => g.Name == name && g.Path == expectedPath);
            return winner ?? throw new HttpRequestException("Group conflict without an identifiable winner.");
        }
        if (response.StatusCode != HttpStatusCode.Created)
            response.EnsureSuccessStatusCode();
        // A subsequent read is intentionally authoritative: never infer a Keycloak group ID.
        return (await GroupsAsync(listPath, ct))
            .FirstOrDefault(g => g.Name == name && g.Path == expectedPath)
            ?? throw new HttpRequestException("Keycloak group not visible after creation.");
    }

    private async Task<GroupView?> MerchantGroupAsync(Guid merchantId, CancellationToken ct)
    {
        var root = await FindRootAsync(ct);
        if (root is null) return null;
        return (await GroupsAsync($"groups/{root.Id}/children?max=100", ct))
            .FirstOrDefault(g => g.Name == merchantId.ToString("D"));
    }

    public async Task<IReadOnlyList<MerchantView>> ListAsync(Guid userId, CancellationToken ct)
    {
        var groups = await GroupsAsync($"users/{userId:D}/groups?max=1000", ct);
        var merchants = new List<MerchantView>();
        foreach (var roleGroup in groups.Where(g => g.Path is not null &&
                     (g.Path.EndsWith("/ADMIN", StringComparison.Ordinal) ||
                      g.Path.EndsWith("/OPERATOR", StringComparison.Ordinal) ||
                      g.Path.EndsWith("/INACTIVE", StringComparison.Ordinal))))
        {
            var parts = roleGroup.Path!.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || parts[0] != RootName || !Guid.TryParse(parts[1], out var id))
                continue;
            var merchantGroup = await MerchantGroupAsync(id, ct);
            if (merchantGroup is null) continue;
            var name = merchantGroup.Attributes?.GetValueOrDefault("displayName")?.FirstOrDefault();
            merchants.Add(new MerchantView(id, name ?? id.ToString("D"), parts[2]));
        }
        // Conflicting role-group snapshots cannot grant access through the directory.
        return merchants.GroupBy(x => x.Id)
            .Where(group => group.Select(member => member.Role).Distinct().Count() == 1)
            .Select(group => group.First())
            .Where(merchant => merchant.Role is AdminRole or OperatorRole).ToArray();
    }

    public async Task<IReadOnlyList<MerchantMemberView>> ListMembersAsync(Guid merchantId, CancellationToken ct)
    {
        var merchant = await MerchantGroupAsync(merchantId, ct);
        if (merchant is null) return [];
        var roles = await GroupsAsync($"groups/{merchant.Id}/children?max=100", ct);
        var members = new List<MerchantMemberView>();
        foreach (var role in roles.Where(g => g.Name is AdminRole or OperatorRole or InactiveRole))
        {
            // Keycloak is the source of truth for the actual accounts, not the role
            // of the platform administrator querying the merchant directory.
            using var response = await SendAsync(HttpMethod.Get,
                $"groups/{role.Id}/members?first=0&max=100", null, ct);
            response.EnsureSuccessStatusCode();
            var users = await response.Content.ReadFromJsonAsync<List<UserView>>(cancellationToken: ct) ?? [];
            members.AddRange(users.Select(u => new MerchantMemberView(u.Username, role.Name)));
        }
        return members.Distinct().OrderBy(m => m.Role).ThenBy(m => m.Username).ToArray();
    }

    public async Task<bool> RenameMerchantAsync(Guid merchantId, string displayName, CancellationToken ct)
    {
        var merchant = await MerchantGroupAsync(merchantId, ct);
        if (merchant is null) return false;
        var attributes = merchant.Attributes is null
            ? new Dictionary<string, string[]>()
            : new Dictionary<string, string[]>(merchant.Attributes);
        attributes["displayName"] = [displayName];
        using var response = await SendAsync(HttpMethod.Put, $"groups/{merchant.Id}",
            new { name = merchant.Name, attributes }, ct);
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> AddOperatorAsync(Guid merchantId, string username,
        string password, CancellationToken ct)
    {
        // Do not silently attach an existing realm user to a different merchant.
        if ((await UsersAsync(username, ct)).Count > 0)
            throw new MerchantUsernameConflictException();
        return await AddMemberAsync(merchantId, username, password, OperatorRole, ct);
    }

    public async Task<bool> RenameMemberAsync(Guid merchantId, string username,
        string nextUsername, string bootstrapUsername, CancellationToken ct)
    {
        if (string.Equals(username, bootstrapUsername, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(nextUsername, bootstrapUsername, StringComparison.OrdinalIgnoreCase))
            throw new MerchantProtectedAccountException();
        var member = await LookupMembershipAsync(merchantId, username, ct);
        if (member is null) return false;
        if (string.Equals(username, nextUsername, StringComparison.Ordinal))
            return true;
        // A merchant administrator may not rename a shared account used by
        // another merchant: username is realm-global, not merchant-scoped.
        var assignedGroups = await GroupsAsync(
            $"users/{member.Value.UserId:D}/groups?max=1000", ct);
        var ownPrefix = $"/merchants/{merchantId:D}/";
        if (assignedGroups.Any(group => group.Path is not null &&
            group.Path.StartsWith("/merchants/", StringComparison.Ordinal) &&
            !group.Path.StartsWith(ownPrefix, StringComparison.Ordinal)))
            throw new MerchantProtectedAccountException();
        if ((await UsersAsync(nextUsername, ct)).Count > 0)
            throw new MerchantUsernameConflictException();

        // Update only the Keycloak username. Keep user UUID, passwords, group
        // memberships, and all financial history unchanged.
        using var response = await SendAsync(HttpMethod.Put,
            $"users/{member.Value.UserId:D}", new { username = nextUsername }, ct);
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new MerchantUsernameConflictException();
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> ChangeMemberRoleAsync(Guid merchantId, string username,
        string nextRole, string bootstrapUsername, CancellationToken ct)
    {
        if (string.Equals(username, bootstrapUsername, StringComparison.OrdinalIgnoreCase))
            throw new MerchantProtectedAccountException();
        var member = await LookupMembershipAsync(merchantId, username, ct);
        if (member is null) return false;
        if (member.Value.Role.Name == nextRole) return true;
        var merchant = await MerchantGroupAsync(merchantId, ct);
        if (merchant is null) return false;
        // A dormant account retains its Keycloak identity and can be reactivated.
        var destination = await GetOrCreateGroupAsync(merchant, nextRole, ct);
        // Assign first, then remove the old group. If removal fails, undo the new role.
        await AssignAsync(member.Value.UserId, destination.Id, ct);
        try
        {
            await UnassignAsync(member.Value.UserId, member.Value.Role.Id, ct);
        }
        catch
        {
            await UnassignAsync(member.Value.UserId, destination.Id, ct);
            throw;
        }
        return true;
    }

    public async Task<bool> DeactivateMemberAsync(Guid merchantId, string username,
        string bootstrapUsername, CancellationToken ct)
    {
        if (string.Equals(username, bootstrapUsername, StringComparison.OrdinalIgnoreCase))
            throw new MerchantProtectedAccountException();
        var member = await LookupMembershipAsync(merchantId, username, ct);
        if (member is null) return false;
        if (member.Value.Role.Name == InactiveRole) return true;
        var merchant = await MerchantGroupAsync(merchantId, ct);
        if (merchant is null) return false;
        var inactive = await GetOrCreateGroupAsync(merchant, InactiveRole, ct);
        // Assign a non-privileged state before revoking the active group.
        await AssignAsync(member.Value.UserId, inactive.Id, ct);
        try
        {
            await UnassignAsync(member.Value.UserId, member.Value.Role.Id, ct);
        }
        catch
        {
            await UnassignAsync(member.Value.UserId, inactive.Id, ct);
            throw;
        }
        return true;
    }

    private async Task<(Guid UserId, GroupView Role)?> LookupMembershipAsync(
        Guid merchantId, string username, CancellationToken ct)
    {
        var users = await UsersAsync(username, ct);
        if (users.Count != 1 || !Guid.TryParse(users[0].Id, out var userId) ||
            !string.Equals(users[0].Username, username, StringComparison.OrdinalIgnoreCase))
            return null;
        var merchant = await MerchantGroupAsync(merchantId, ct);
        if (merchant is null) return null;
        var roleGroups = await GroupsAsync($"groups/{merchant.Id}/children?max=100", ct);
        var linkedGroups = await GroupsAsync($"users/{userId:D}/groups?max=1000", ct);
        // Never guess a role when Keycloak exposes overlapping memberships.
        // A partial role transition must fail closed until repaired.
        var activeRoles = roleGroups.Where(g =>
            (g.Name is AdminRole or OperatorRole or InactiveRole) &&
            linkedGroups.Any(link => link.Id == g.Id)).ToArray();
        if (activeRoles.Length > 1)
            throw new HttpRequestException("Conflicting merchant memberships require reconciliation.");
        return activeRoles.Length == 0 ? null : (userId, activeRoles[0]);
    }

    private async Task UnassignAsync(Guid userId, string groupId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Delete,
            $"users/{userId:D}/groups/{groupId}", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> IsAdminAsync(Guid userId, Guid merchantId, CancellationToken ct)
    {
        var groups = await GroupsAsync($"users/{userId:D}/groups?max=1000", ct);
        var prefix = $"/merchants/{merchantId:D}/";
        var currentRoles = groups.Where(g => g.Path is not null &&
            g.Path.StartsWith(prefix, StringComparison.Ordinal) &&
            (g.Path == prefix + AdminRole || g.Path == prefix + OperatorRole ||
             g.Path == prefix + InactiveRole)).Select(g => g.Path).Distinct().ToArray();
        return currentRoles.Length == 1 && currentRoles[0] == prefix + AdminRole;
    }

    public async Task<MerchantView> CreateAsync(string name, Guid ownerId, CancellationToken ct)
    {
        var root = await GetOrCreateGroupAsync(null, RootName, ct);
        var id = Guid.NewGuid();
        var merchant = await GetOrCreateGroupAsync(root, id.ToString("D"), ct);
        using (var update = await SendAsync(HttpMethod.Put, $"groups/{merchant.Id}",
            new { name = merchant.Name, attributes = new Dictionary<string, string[]> {
                ["displayName"] = [name] } }, ct))
            update.EnsureSuccessStatusCode();
        var admin = await GetOrCreateGroupAsync(merchant, AdminRole, ct);
        await GetOrCreateGroupAsync(merchant, OperatorRole, ct);
        await GetOrCreateGroupAsync(merchant, InactiveRole, ct);
        await AssignAsync(ownerId, admin.Id, ct);
        return new MerchantView(id, name, AdminRole);
    }

    public async Task<MerchantView> CreateWithOperatorAsync(
        string name, Guid ownerId, string username, string password, CancellationToken ct)
    {
        // Usernames belong to the realm, not to an individual merchant. Never link an
        // existing account automatically, because it could grant access to the wrong person.
        if ((await UsersAsync(username, ct)).Count > 0)
            throw new MerchantUsernameConflictException();

        var created = await CreateAsync(name, ownerId, ct);
        var assigned = await AddMemberAsync(created.Id, username, password, OperatorRole, ct);
        if (!assigned)
            throw new HttpRequestException("Merchant operator provisioning incomplete.");
        return created;
    }

    private async Task AssignAsync(Guid userId, string groupId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Put, $"users/{userId:D}/groups/{groupId}", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public Task<bool> AddAdminAsync(Guid merchantId, string username,
        string? temporaryPassword, CancellationToken ct) =>
        AddMemberAsync(merchantId, username, temporaryPassword, AdminRole, ct);

    private async Task<bool> AddMemberAsync(Guid merchantId, string username,
        string? password, string role, CancellationToken ct)
    {
        var merchant = await MerchantGroupAsync(merchantId, ct);
        if (merchant is null) return false;
        var group = (await GroupsAsync($"groups/{merchant.Id}/children?max=100", ct))
            .FirstOrDefault(g => g.Name == role);
        if (group is null) throw new HttpRequestException("Merchant role group missing.");

        var users = await UsersAsync(username, ct);
        var createdByThisRequest = false;
        if (users.Count == 0 && !string.IsNullOrWhiteSpace(password))
        {
            using var created = await SendAsync(HttpMethod.Post, "users", new
            {
                username, enabled = true,
                credentials = new[] { new {
                    type = "password", value = password,
                    temporary = role == AdminRole
                } }
            }, ct);
            if (created.StatusCode == HttpStatusCode.Conflict)
                throw new MerchantUsernameConflictException();
            created.EnsureSuccessStatusCode();
            createdByThisRequest = true;
            users = await UsersAsync(username, ct);
        }

        if (users.Count == 0 && !createdByThisRequest)
            return false; // Existing ADMIN flow may intentionally omit a new password.
        if (users.Count != 1 ||
            !string.Equals(users[0].Username, username, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(users[0].Id, out var userId))
            throw new HttpRequestException("Keycloak user resolution ambiguous; manual reconciliation required.");

        try
        {
            await AssignAsync(userId, group.Id, ct);
        }
        catch (HttpRequestException) when (createdByThisRequest)
        {
            // Only compensate a user created by this request. Never delete an
            // existing realm account, even when the requested username matches.
            await DeleteNewlyCreatedUserAsync(userId);
            throw;
        }
        return true;
    }

    private async Task DeleteNewlyCreatedUserAsync(Guid userId)
    {
        // A request may have been cancelled while the assignment failed.
        // Attempt bounded cleanup without reusing its cancelled token.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            using var response = await SendAsync(HttpMethod.Delete,
                $"users/{userId:D}", null, timeout.Token);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new HttpRequestException(
                "Keycloak provisioning compensation failed; manual reconciliation required.", ex);
        }
    }

    private async Task<List<UserView>> UsersAsync(string username, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"users?username={Uri.EscapeDataString(username)}&exact=true&max=2", null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<UserView>>(cancellationToken: ct) ?? [];
    }
}

public sealed record MerchantView(Guid Id, string Name, string Role);
public sealed record MerchantMemberView(string Username, string Role);

public sealed class MerchantUsernameConflictException : Exception { }
public sealed class MerchantProtectedAccountException : Exception { }
