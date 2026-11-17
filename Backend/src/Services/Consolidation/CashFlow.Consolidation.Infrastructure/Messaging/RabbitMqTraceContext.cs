using System.Diagnostics;
using System.Text;

namespace CashFlow.Consolidation.Infrastructure.Messaging;

/// <summary>Converts untrusted RabbitMQ W3C trace headers into validated parent span identifiers.</summary>
public static class RabbitMqTraceContext
{
    public static string? GetParentId(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue("traceparent", out var header))
            return null;

        var candidate = header switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> memory => Encoding.UTF8.GetString(memory.Span),
            string value => value,
            _ => null
        };

        return candidate is not null &&
            ActivityContext.TryParse(candidate, null, out _)
            ? candidate : null;
    }
}
