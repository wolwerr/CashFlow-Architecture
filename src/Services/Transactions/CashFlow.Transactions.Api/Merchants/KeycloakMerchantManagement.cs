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
                      g.Path.EndsWith("/OPERATOR", StringComparison.Ordinal))))
        {
            var parts = roleGroup.Path!.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || parts[0] != RootName || !Guid.TryParse(parts[1], out var id))
                continue;
            var merchantGroup = await MerchantGroupAsync(id, ct);
            if (merchantGroup is null) continue;
            var name = merchantGroup.Attributes?.GetValueOrDefault("displayName")?.FirstOrDefault();
            merchants.Add(new MerchantView(id, name ?? id.ToString("D"), parts[2]));
        }
        return merchants.DistinctBy(x => x.Id).ToArray();
    }

    public async Task<bool> IsAdminAsync(Guid userId, Guid merchantId, CancellationToken ct)
    {
        var groups = await GroupsAsync($"users/{userId:D}/groups?max=1000", ct);
        return groups.Any(g => g.Path == $"/merchants/{merchantId:D}/ADMIN");
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
        var admin = await GetOrCreateGroupAsync(merchant, "ADMIN", ct);
        await GetOrCreateGroupAsync(merchant, "OPERATOR", ct);
        await AssignAsync(ownerId, admin.Id, ct);
        return new MerchantView(id, name, "ADMIN");
    }

    public async Task<MerchantView> CreateWithOperatorAsync(
        string name, Guid ownerId, string username, string password, CancellationToken ct)
    {
        // Usernames belong to the realm, not to an individual merchant. Never link an
        // existing account automatically, because it could grant access to the wrong person.
        if ((await UsersAsync(username, ct)).Count > 0)
            throw new MerchantUsernameConflictException();

        var created = await CreateAsync(name, ownerId, ct);
        var assigned = await AddMemberAsync(created.Id, username, password, "OPERATOR", ct);
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
        AddMemberAsync(merchantId, username, temporaryPassword, "ADMIN", ct);

    private async Task<bool> AddMemberAsync(Guid merchantId, string username,
        string? password, string role, CancellationToken ct)
    {
        var merchant = await MerchantGroupAsync(merchantId, ct);
        if (merchant is null) return false;
        var group = (await GroupsAsync($"groups/{merchant.Id}/children?max=100", ct))
            .FirstOrDefault(g => g.Name == role);
        if (group is null) throw new HttpRequestException("Merchant role group missing.");
        var users = await UsersAsync(username, ct);
        if (users.Count == 0 && !string.IsNullOrWhiteSpace(password))
        {
            // New merchant credentials are usable immediately in the local MVP.
            // Password strength and recovery hardening are deferred and documented.
            using var created = await SendAsync(HttpMethod.Post, "users", new
            {
                username, enabled = true,
                credentials = new[] { new {
                    type = "password", value = password,
                    temporary = role == "ADMIN"
                } }
            }, ct);
            created.EnsureSuccessStatusCode();
            users = await UsersAsync(username, ct);
        }
        if (users.Count != 1) return false;
        if (!string.Equals(users[0].Username, username, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!Guid.TryParse(users[0].Id, out var userId)) return false;
        await AssignAsync(userId, group.Id, ct);
        return true;
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

public sealed class MerchantUsernameConflictException : Exception { }
