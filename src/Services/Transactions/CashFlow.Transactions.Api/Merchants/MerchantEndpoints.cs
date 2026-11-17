using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;

namespace CashFlow.Transactions.Api.Merchants;

public static class MerchantEndpoints
{
    private const string MembersRoute = "/members";
    private static readonly TimeSpan UsernameRegexTimeout = TimeSpan.FromMilliseconds(100);
    public static IEndpointRouteBuilder MapMerchantManagement(this IEndpointRouteBuilder app)
    {
        var routes = app.MapGroup("/api/v1/merchants").RequireAuthorization();
        routes.MapGet("/", ListAsync);
        routes.MapGet("/permissions", Permissions);
        routes.MapGet(MembersRoute, MembersAsync);
        routes.MapPost(MembersRoute, AddOperatorAsync);
        routes.MapPut(MembersRoute, UpdateMemberAsync);
        routes.MapPut("/members/username", RenameMemberAsync);
        routes.MapDelete(MembersRoute, DeleteMemberAsync);
        routes.MapPut("/details", RenameAsync);
        routes.MapPost("/", CreateAsync);
        routes.MapPost("/{merchantId:guid}/admins", AddAdminAsync);
        return app;
    }

    private static Guid Actor(HttpContext context) =>
        Guid.TryParse(context.User.FindFirstValue("sub"), out var subject) ? subject : Guid.Empty;

    private static bool IsBootstrap(HttpContext context, KeycloakManagementSettings settings,
        IWebHostEnvironment environment) =>
        environment.IsDevelopment() &&
        !string.IsNullOrWhiteSpace(settings.BootstrapUsername) &&
        string.Equals(settings.BootstrapUsername,
            context.User.FindFirstValue("preferred_username"), StringComparison.Ordinal);

    private static IResult Permissions(HttpContext context,
        IOptions<KeycloakManagementSettings> settings, IWebHostEnvironment environment) =>
        Results.Ok(new { can_manage_merchants = Actor(context) != Guid.Empty &&
            IsBootstrap(context, settings.Value, environment) });

    private static async Task<IResult> ListAsync(HttpContext context,
        KeycloakMerchantManagement management, CancellationToken ct)
    {
        if (Actor(context) == Guid.Empty) return Results.Unauthorized();
        try { return Results.Ok(await management.ListAsync(Actor(context), ct)); }
        catch (HttpRequestException) { return Results.Problem(statusCode: 503, title: "Merchant directory unavailable."); }
    }

    private static async Task<IResult> MembersAsync([FromHeader(Name = "X-Merchant-ID")] Guid merchantId, HttpContext context,
        KeycloakMerchantManagement management, CancellationToken ct)
    {
        var actor = Actor(context);
        if (actor == Guid.Empty) return Results.Unauthorized();
        if (merchantId == Guid.Empty) return Results.BadRequest();
        try
        {
            // Membership details must never be disclosed to another merchant.
            if (!await management.IsAdminAsync(actor, merchantId, ct))
                return Results.Forbid();
            return Results.Ok(await management.ListMembersAsync(merchantId, ct));
        }
        catch (HttpRequestException)
        {
            return Results.Problem(statusCode: 503, title: "Merchant members unavailable.");
        }
    }

    private static bool ValidUsername(string? username) => username is not null &&
        Regex.IsMatch(username, "^[a-zA-Z][a-zA-Z0-9._-]{2,63}$", RegexOptions.CultureInvariant, UsernameRegexTimeout);

    private static async Task<IResult> RenameAsync([FromHeader(Name = "X-Merchant-ID")] Guid merchantId,
        RenameMerchantRequest? request, HttpContext context, KeycloakMerchantManagement management,
        CancellationToken ct)
    {
        if (Actor(context) == Guid.Empty) return Results.Unauthorized();
        if (merchantId == Guid.Empty || string.IsNullOrWhiteSpace(request?.Name) ||
            request.Name.Trim().Length > 100) return Results.BadRequest();
        try
        {
            if (!await management.IsAdminAsync(Actor(context), merchantId, ct))
                return Results.Forbid();
            return await management.RenameMerchantAsync(merchantId, request.Name.Trim(), ct)
                ? Results.NoContent() : Results.NotFound();
        }
        catch (HttpRequestException) { return Results.Problem(statusCode: 503); }
    }

    private static async Task<IResult> AddOperatorAsync([FromHeader(Name = "X-Merchant-ID")] Guid merchantId,
        NewMerchantMemberRequest? request, HttpContext context, KeycloakMerchantManagement management,
        CancellationToken ct)
    {
        if (Actor(context) == Guid.Empty) return Results.Unauthorized();
        if (merchantId == Guid.Empty || !ValidUsername(request?.Username) ||
            request?.Password is null || request.Password.Length is < 12 or > 128)
            return Results.BadRequest();
        try
        {
            if (!await management.IsAdminAsync(Actor(context), merchantId, ct))
                return Results.Forbid();
            return await management.AddOperatorAsync(merchantId, request.Username!, request.Password, ct)
                ? Results.NoContent() : Results.NotFound();
        }
        catch (MerchantUsernameConflictException) { return Results.Conflict(new { title = "Username already exists." }); }
        catch (HttpRequestException) { return Results.Problem(statusCode: 503); }
    }

    private static async Task<IResult> UpdateMemberAsync([FromHeader(Name = "X-Merchant-ID")] Guid merchantId,
        ChangeMerchantMemberRequest? request, HttpContext context, KeycloakMerchantManagement management,
        IOptions<KeycloakManagementSettings> settings, CancellationToken ct)
    {
        if (Actor(context) == Guid.Empty) return Results.Unauthorized();
        if (merchantId == Guid.Empty || !ValidUsername(request?.Username) ||
            request?.Role is not ("ADMIN" or "OPERATOR")) return Results.BadRequest();
        try
        {
            if (!await management.IsAdminAsync(Actor(context), merchantId, ct))
                return Results.Forbid();
            return await management.ChangeMemberRoleAsync(merchantId, request.Username!,
                request.Role, settings.Value.BootstrapUsername, ct)
                ? Results.NoContent() : Results.NotFound();
        }
        catch (MerchantProtectedAccountException) { return Results.Conflict(new { title = "Bootstrap account is protected." }); }
        catch (HttpRequestException) { return Results.Problem(statusCode: 503); }
    }

    private static async Task<IResult> RenameMemberAsync([FromHeader(Name = "X-Merchant-ID")] Guid merchantId,
        RenameMerchantMemberRequest? request, HttpContext context, KeycloakMerchantManagement management,
        IOptions<KeycloakManagementSettings> settings, CancellationToken ct)
    {
        if (Actor(context) == Guid.Empty) return Results.Unauthorized();
        if (merchantId == Guid.Empty || request is null || !ValidUsername(request.Username) ||
            !ValidUsername(request.NewUsername)) return Results.BadRequest();
        try
        {
            if (!await management.IsAdminAsync(Actor(context), merchantId, ct))
                return Results.Forbid();
            return await management.RenameMemberAsync(merchantId, request.Username!,
                request.NewUsername!, settings.Value.BootstrapUsername, ct)
                ? Results.NoContent() : Results.NotFound();
        }
        catch (MerchantProtectedAccountException)
        {
            return Results.Conflict(new { title = "Bootstrap account is protected." });
        }
        catch (MerchantUsernameConflictException)
        {
            return Results.Conflict(new { title = "Username already exists." });
        }
        catch (MerchantUsernameChangeNotAllowedException)
        {
            return Results.Conflict(new { title = "Keycloak rejected the username update. Check the realm's Edit username setting." });
        }
        catch (HttpRequestException) { return Results.Problem(statusCode: 503); }
    }

    private static async Task<IResult> DeleteMemberAsync([FromHeader(Name = "X-Merchant-ID")] Guid merchantId,
        [FromBody] RemoveMerchantMemberRequest? request, HttpContext context, KeycloakMerchantManagement management,
        IOptions<KeycloakManagementSettings> settings, CancellationToken ct)
    {
        if (Actor(context) == Guid.Empty) return Results.Unauthorized();
        if (merchantId == Guid.Empty || request is null || !ValidUsername(request.Username))
            return Results.BadRequest();
        try
        {
            if (!await management.IsAdminAsync(Actor(context), merchantId, ct))
                return Results.Forbid();
            return await management.DeactivateMemberAsync(merchantId, request.Username!,
                settings.Value.BootstrapUsername, ct)
                ? Results.NoContent() : Results.NotFound();
        }
        catch (MerchantProtectedAccountException) { return Results.Conflict(new { title = "Bootstrap account is protected." }); }
        catch (HttpRequestException) { return Results.Problem(statusCode: 503); }
    }

    private static async Task<IResult> CreateAsync(CreateMerchantRequest? request,
        HttpContext context, KeycloakMerchantManagement management,
        IOptions<KeycloakManagementSettings> options, IWebHostEnvironment environment,
        CancellationToken ct)
    {
        if (Actor(context) == Guid.Empty) return Results.Unauthorized();
        if (request is null || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 100)
            return Results.BadRequest(new { title = "Merchant name must have 1 to 100 characters." });

        // First merchant bootstrap is restricted to an explicitly configured local account.
        // Never grant merchant creation to an arbitrary authenticated user.
        if (!IsBootstrap(context, options.Value, environment))
            return Results.Forbid();
        if (request.Username is null ||
            !ValidUsername(request.Username) ||
            request.Password is null || request.Password.Length is < 12 or > 128)
            return Results.BadRequest(new { title = "Unique merchant username and password (12-128 characters) are required." });

        try
        {
            var result = await management.CreateWithOperatorAsync(request.Name.Trim(),
                Actor(context), request.Username, request.Password, ct);
            return Results.Created($"/api/v1/merchants/{result.Id:D}", result);
        }
        catch (MerchantUsernameConflictException)
        {
            return Results.Conflict(new { title = "Username already exists in this realm." });
        }
        catch (HttpRequestException)
        {
            return Results.Problem(statusCode: 503, title: "Merchant provisioning unavailable.");
        }
    }

    private static async Task<IResult> AddAdminAsync(Guid merchantId, AddMerchantAdminRequest? request,
        HttpContext context, KeycloakMerchantManagement management, CancellationToken ct)
    {
        if (Actor(context) == Guid.Empty) return Results.Unauthorized();
        if (merchantId == Guid.Empty || string.IsNullOrWhiteSpace(request?.Username) ||
            request.Username.Length > 100 ||
            (request.TemporaryPassword is not null &&
             (request.TemporaryPassword.Length < 12 || request.TemporaryPassword.Length > 128)))
            return Results.BadRequest(new { title = "Invalid administrator information." });

        try
        {
            if (!await management.IsAdminAsync(Actor(context), merchantId, ct))
                return Results.Forbid();
            var assigned = await management.AddAdminAsync(merchantId,
                request.Username.Trim(), request.TemporaryPassword, ct);
            return assigned ? Results.NoContent()
                : Results.NotFound(new { title = "Merchant or user not found." });
        }
        catch (HttpRequestException)
        {
            return Results.Problem(statusCode: 503, title: "Administrator provisioning unavailable.");
        }
    }
}

public sealed record CreateMerchantRequest(string Name, string? Username, string? Password);
public sealed record AddMerchantAdminRequest(string Username, string? TemporaryPassword);

public sealed record RenameMerchantRequest(string Name);
public sealed record NewMerchantMemberRequest(string? Username, string? Password);
public sealed record ChangeMerchantMemberRequest(string? Username, string? Role);
public sealed record RemoveMerchantMemberRequest(string? Username);
public sealed record RenameMerchantMemberRequest(string? Username, string? NewUsername);
