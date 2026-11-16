using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace CashFlow.Transactions.Api.Authentication;

public static class CashFlowAuthenticationExtensions
{
    public static IServiceCollection AddCashFlowAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var settings = configuration.GetSection(CashFlowJwtSettings.SectionName)
            .Get<CashFlowJwtSettings>() ?? new CashFlowJwtSettings();
        settings.Validate(environment.IsDevelopment());

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = settings.Authority.TrimEnd('/');
                options.Audience = settings.Audience;
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                if (settings.MetadataAddress is not null)
                    options.MetadataAddress = settings.MetadataAddress;

                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Authority.TrimEnd('/'),
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateIssuerSigningKey = true,
                    ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
                    RequireSignedTokens = true,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub"
                };
            });

        services.AddAuthorization(options =>
        {
            // No anonymous API endpoint can be added accidentally. /health is explicitly public.
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });
        return services;
    }
}
