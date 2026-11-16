using CashFlow.Security.Observability;
using CashFlow.Security.Authentication;
using CashFlow.Security.Keycloak;
using CashFlow.Consolidation.Api.Endpoints;
using CashFlow.Consolidation.Application.Gateways;
using CashFlow.Consolidation.Application.UseCases.GetDailyConsolidation;
using CashFlow.Consolidation.Application.UseCases.ProcessTransaction;
using CashFlow.Consolidation.Infrastructure.Gateways;
using CashFlow.Consolidation.Infrastructure.Messaging;
using CashFlow.Consolidation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();
builder.Services.AddCashFlowAuthentication(builder.Configuration, builder.Environment);

var authorization = builder.Configuration.GetSection(KeycloakAuthorizationSettings.SectionName)
    .Get<KeycloakAuthorizationSettings>() ?? new KeycloakAuthorizationSettings();
authorization.Validate(builder.Environment.IsDevelopment());
builder.Services.AddSingleton(Options.Create(authorization));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IConsolidationAuthorizationGateway, KeycloakConsolidationAuthorizationGateway>(
    client =>
    {
        client.BaseAddress = new Uri(authorization.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(3);
    });

var connection = builder.Configuration.GetConnectionString("Consolidation");
if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("ConnectionStrings:Consolidation is required.");
builder.Services.AddDbContext<ConsolidationDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddScoped<IConsolidationGateway, ConsolidationGateway>();
builder.Services.AddScoped<ProcessTransactionUseCase>();
builder.Services.AddScoped<GetDailyConsolidationUseCase>();

var rabbit = builder.Configuration.GetSection(RabbitMqConsumerSettings.SectionName)
    .Get<RabbitMqConsumerSettings>() ?? new RabbitMqConsumerSettings();
rabbit.Validate();
builder.Services.AddSingleton(Options.Create(rabbit));
if (builder.Configuration.GetValue<bool>("Consolidation:EnableConsumer"))
    builder.Services.AddHostedService<ConsolidationConsumerWorker>();

var app = builder.Build();
if (app.Environment.IsDevelopment() &&
    app.Configuration.GetValue<bool>("Consolidation:ApplyMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ConsolidationDbContext>();
    await db.Database.MigrateAsync();
}
app.UseCashFlowRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapDailyConsolidation();
app.Run();

public partial class Program { }
