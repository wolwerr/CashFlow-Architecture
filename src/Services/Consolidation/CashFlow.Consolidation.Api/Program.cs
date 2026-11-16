var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();
var app = builder.Build();
app.MapHealthChecks("/health");
// Financial and protected API routes are intentionally NOT exposed until
// JWT verification, CashFlow merchant authorization and persistence exist.
app.Run();

public partial class Program { }
