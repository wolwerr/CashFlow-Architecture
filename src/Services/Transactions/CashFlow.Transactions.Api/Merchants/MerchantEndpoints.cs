using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace CashFlow.Transactions.Api.Merchants;

public static class MerchantEndpoints
{
    public static IEndpointRouteBuilder MapMerchantManagement(this IEndpointRouteBuilder app)
    {
        var routes = app.MapGroup("/api/v1/merchants").RequireAuthorization();
        routes.MapGet("/", ListAsync);
        routes.MapGet("/permissions", Permissions);
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

    private static async Task<IResult> CreateAsync(CreateMerchantRequest? request,
        HttpContext context, KeycloakMerchantManagement management,
        IOptions<KeycloakManagementSettings> options, IWebHostEnvironment environment,
        CancellationToken ct)
    {
        if (Actor(context) == Guid.Empty) return Results.Unauthorized();
        if (request is null || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 100 ||
            request.Username is null ||
            !Regex.IsMatch(request.Username, "^[a-zA-Z][a-zA-Z0-9._-]{2,63}$") ||
            request.Password is null || request.Password.Length is < 12 or > 128)
            return Results.BadRequest(new { title = "Merchant name, unique username and password (12-128 characters) are required." });

        // First merchant bootstrap is restricted to an explicitly configured local account.
        // Never grant merchant creation to an arbitrary authenticated user.
        if (!IsBootstrap(context, options.Value, environment))
            return Results.Forbid();

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
