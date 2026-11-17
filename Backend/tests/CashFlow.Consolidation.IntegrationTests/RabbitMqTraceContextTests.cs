using System.Diagnostics;
using System.Text;
using CashFlow.Consolidation.Infrastructure.Messaging;
using Xunit;

namespace CashFlow.Consolidation.IntegrationTests;

public sealed class RabbitMqTraceContextTests
{
    private const string ValidParent = "00-11111111111111111111111111111111-2222222222222222-01";

    [Fact]
    public void MissingHeadersDoNotCreateParent()
    {
        Assert.Null(RabbitMqTraceContext.GetParentId(null));
        Assert.Null(RabbitMqTraceContext.GetParentId(new Dictionary<string, object?>()));
    }

    [Fact]
    public void AcceptsValidUtf8Bytes()
    {
        var headers = new Dictionary<string, object?>
        {
            ["traceparent"] = Encoding.UTF8.GetBytes(ValidParent)
        };
        Assert.Equal(ValidParent, RabbitMqTraceContext.GetParentId(headers));
    }

    [Fact]
    public void AcceptsValidMemoryAndString()
    {
        var bytes = Encoding.UTF8.GetBytes(ValidParent);
        var memoryHeaders = new Dictionary<string, object?>
        {
            ["traceparent"] = new ReadOnlyMemory<byte>(bytes)
        };
        Assert.Equal(ValidParent, RabbitMqTraceContext.GetParentId(memoryHeaders));
        Assert.Equal(ValidParent, RabbitMqTraceContext.GetParentId(
            new Dictionary<string, object?> { ["traceparent"] = ValidParent }));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("00-00000000000000000000000000000000-2222222222222222-01")]
    public void RejectsMalformedHeader(string header)
    {
        Assert.Null(RabbitMqTraceContext.GetParentId(
            new Dictionary<string, object?> { ["traceparent"] = header }));
    }

    [Fact]
    public void RejectsUnexpectedHeaderType()
    {
        Assert.Null(RabbitMqTraceContext.GetParentId(
            new Dictionary<string, object?> { ["traceparent"] = 7 }));
    }
}
