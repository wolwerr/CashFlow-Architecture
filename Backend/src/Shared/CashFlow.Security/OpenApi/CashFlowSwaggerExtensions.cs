using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CashFlow.Security.OpenApi;

/// <summary>Local-only interactive OpenAPI documentation for CashFlow services.</summary>
public static class CashFlowSwaggerExtensions
{
    public static IServiceCollection AddCashFlowSwagger(
        this IServiceCollection services, IWebHostEnvironment environment,
        IConfiguration configuration, string title)
    {
        if (!environment.IsDevelopment())
            return services;

        var authority = configuration["Authentication:Authority"]?.TrimEnd('/');
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var issuer))
            throw new InvalidOperationException(
                "Authentication:Authority must be an absolute URL for Swagger OAuth2.");

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = title,
                Version = "v1",
                Description = "Generated from live CashFlow API endpoints; local Development only."
            });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Paste the Keycloak access token without the Bearer prefix."
            });
            options.AddSecurityDefinition("KeycloakOAuth2", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Description = "Sign in with Keycloak using Authorization Code and PKCE (S256).",
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri(issuer, issuer.AbsolutePath.TrimEnd('/') +
                            "/protocol/openid-connect/auth"),
                        TokenUrl = new Uri(issuer, issuer.AbsolutePath.TrimEnd('/') +
                            "/protocol/openid-connect/token"),
                        Scopes = new Dictionary<string, string>
                        {
                            ["openid"] = "OpenID Connect authentication"
                        }
                    }
                }
            });
            // Each requirement is an alternative (OAuth2 login OR manual Bearer token).
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("KeycloakOAuth2", document)] = ["openid"]
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        });
        return services;
    }

    public static WebApplication UseCashFlowSwagger(
        this WebApplication app, string proxyPrefix)
    {
        if (!app.Environment.IsDevelopment())
            return app;

        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = "swagger";
            options.SwaggerEndpoint($"/swagger/{proxyPrefix}/v1/swagger.json",
                $"{proxyPrefix} API v1");
            options.DocumentTitle = $"CashFlow | {proxyPrefix} API";
            options.OAuthClientId("cashflow-spa");
            options.OAuthAppName($"CashFlow | {proxyPrefix} API");
            options.OAuthUsePkce();
            options.OAuthScopes("openid");
            options.OAuth2RedirectUrl(
                $"http://localhost:8080/swagger/{proxyPrefix}/oauth2-redirect.html");
        });
        return app;
    }
}
