using CashFlow.Security.Authentication;
using CashFlow.Security.Keycloak;
using CashFlow.Transactions.Api.Endpoints;
using CashFlow.Transactions.Application.Gateways;
using CashFlow.Transactions.Application.UseCases.CreateTransaction;
using CashFlow.Transactions.Infrastructure.Gateways;
using CashFlow.Transactions.Infrastructure.Messaging;
using CashFlow.Transactions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();
builder.Services.AddCashFlowAuthentication(builder.Configuration, builder.Environment);

var authz = builder.Configuration.GetSection(KeycloakAuthorizationSettings.SectionName)
    .Get<KeycloakAuthorizationSettings>() ?? new KeycloakAuthorizationSettings();
authz.Validate(builder.Environment.IsDevelopment());
builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(authz));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<ICreateTransactionAuthorizationGateway, KeycloakMerchantAuthorizationGateway>(
    client =>
    {
        client.BaseAddress = new Uri(authz.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(3);
    });
var connection = builder.Configuration.GetConnectionString("Transactions");
if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("ConnectionStrings:Transactions is required.");
builder.Services.AddDbContext<TransactionsDbContext>(o => o.UseNpgsql(connection));
builder.Services.AddScoped<ITransactionRegistrationGateway, TransactionRegistrationGateway>();
builder.Services.AddScoped<CreateTransactionUseCase>();
builder.Services.AddSingleton(TimeProvider.System);

if (builder.Configuration.GetValue<bool>("Transactions:EnableOutboxPublisher"))
{
    var rabbit = builder.Configuration.GetSection(RabbitMqConnectionSettings.SectionName)
        .Get<RabbitMqConnectionSettings>() ?? new RabbitMqConnectionSettings();
    rabbit.Validate();
    builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(rabbit));
    builder.Services.AddHostedService<OutboxPublisherWorker>();
}

var app = builder.Build();
if (app.Environment.IsDevelopment() &&
    app.Configuration.GetValue<bool>("Transactions:ApplyMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>();
    await db.Database.MigrateAsync();
}
// Log only route metadata and timing. Never log query strings, tokens or request bodies.
app.Use(async (context, next) =>
{
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    try
    {
        await next(context);
    }
    finally
    {
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        app.Logger.LogInformation(
            "HTTP {Method} {Route} returned {StatusCode} in {ElapsedMs}ms trace={TraceId}",
            context.Request.Method,
            context.GetEndpoint()?.DisplayName ?? "unmatched",
            context.Response.StatusCode,
            elapsed,
            context.TraceIdentifier);
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapGet("/internal/authentication/check", () => Results.NoContent())
    .RequireAuthorization();
app.MapCreateTransaction();
app.MapTransactionHistory();
app.Run();

public partial class Program { }
