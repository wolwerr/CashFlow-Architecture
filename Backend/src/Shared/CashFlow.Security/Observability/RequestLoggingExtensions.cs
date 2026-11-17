using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CashFlow.Security.Observability;

/// <summary>Shared, privacy-conscious HTTP diagnostics for the local API services.</summary>
public static class RequestLoggingExtensions
{
    public static WebApplication UseCashFlowRequestLogging(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                await next(context);
            }
            finally
            {
                var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                // No URL, query parameters, authorization headers or financial payloads.
                app.Logger.LogInformation(
                    "HTTP {Method} {Route} returned {StatusCode} in {ElapsedMs}ms trace={TraceId}",
                    context.Request.Method,
                    context.GetEndpoint()?.DisplayName ?? "unmatched",
                    context.Response.StatusCode,
                    elapsedMs,
                    context.TraceIdentifier);
            }
        });
        return app;
    }
}
