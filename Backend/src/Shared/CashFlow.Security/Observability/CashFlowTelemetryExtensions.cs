using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CashFlow.Security.Observability;

/// <summary>
/// Opt-in OTLP export. Defaults to disabled so LOCAL MVP behavior stays unchanged.
/// Exporter failures must not block financial requests.
/// </summary>
public static class CashFlowTelemetryExtensions
{
    public static IHostApplicationBuilder AddCashFlowTelemetry(
        this IHostApplicationBuilder builder, string serviceName)
    {
        if (!builder.Configuration.GetValue<bool>("CashFlowTelemetry:Enabled"))
            return builder;

        var resource = ResourceBuilder.CreateDefault()
            .AddService(serviceName, serviceVersion: "1.0.0");

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceVersion: "1.0.0"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options => options.Filter =
                    context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddSource("CashFlow.Transactions.Outbox")
                .AddSource("CashFlow.Consolidation.Consumer")
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter());

        builder.Logging.AddOpenTelemetry(options =>
        {
            options.SetResourceBuilder(resource);
            options.IncludeFormattedMessage = false;
            options.IncludeScopes = true;
            options.AddOtlpExporter();
        });
        return builder;
    }
}
