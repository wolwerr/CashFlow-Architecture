using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace CashFlow.Transactions.Api.Merchants;

public static class MerchantEndpoints
{
    public static IEndpointRouteBuilder MapMerchantManagement(this IEndpointRouteBuilder app)
    {
        var routes = app.MapGroup("/api/v1/merchants").RequireAuthorization();
        routes.MapGet("/", ListAsync);
        routes.MapPost("/", CreateAsync);
        routes.MapPost("/{merchantId:guid}/admins", AddAdminAsync);
        return app;
    }

    private static Guid Actor(HttpContext context) =>
        Guid.TryParse(context.User.FindFirstValue("sub"), out var subject) ? subject : Guid.Empty;

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
        if (request?.Name is null || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 100)
            return Results.BadRequest(new { title = "Merchant name must have 1 to 100 characters." });

        // First merchant bootstrap is restricted to an explicitly configured local account.
        // Never grant merchant creation to an arbitrary authenticated user.
        var configured = options.Value.BootstrapUsername;
        var actorName = context.User.FindFirstValue("preferred_username");
        if (!environment.IsDevelopment() || string.IsNullOrWhiteSpace(configured) ||
            !string.Equals(configured, actorName, StringComparison.Ordinal))
            return Results.Forbid();

        try
        {
            var result = await management.CreateAsync(request.Name.Trim(), Actor(context), ct);
            return Results.Created($"/api/v1/merchants/{result.Id:D}", result);
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

public sealed record CreateMerchantRequest(string Name);
public sealed record AddMerchantAdminRequest(string Username, string? TemporaryPassword);
