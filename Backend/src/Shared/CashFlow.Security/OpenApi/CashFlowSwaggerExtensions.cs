using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CashFlow.Security.OpenApi;

/// <summary>Local-only interactive OpenAPI documentation for CashFlow services.</summary>
public static class CashFlowSwaggerExtensions
{
    public static IServiceCollection AddCashFlowSwagger(
        this IServiceCollection services, IWebHostEnvironment environment, string title)
    {
        if (!environment.IsDevelopment())
            return services;

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
        });
        return app;
    }
}
