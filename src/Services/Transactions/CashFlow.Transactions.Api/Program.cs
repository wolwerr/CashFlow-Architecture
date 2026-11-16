using CashFlow.Transactions.Api.Authentication;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();
builder.Services.AddCashFlowAuthentication(builder.Configuration, builder.Environment);

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();

// Diagnostic route verifies the full JWT pipeline, but has no financial capabilities.
// Merchant membership and RBAC must be implemented before financial endpoints exist.
app.MapGet("/internal/authentication/check", () => Results.NoContent())
    .RequireAuthorization();

app.Run();

public partial class Program { }
