using System.Text;
using CashFlow.Consolidation.Infrastructure.Messaging;
using Xunit;

namespace CashFlow.Consolidation.IntegrationTests;

public sealed class TransactionRecordedEventParserTests
{
    private static string Payload(string type = "CREDIT", string amount = "10.50",
        string currency = "BRL", string version = "1") => $$"""
        {
          "event_id": "{{Guid.NewGuid():D}}",
          "event_type": "transaction.recorded",
          "schema_version": "{{version}}",
          "correlation_id": "trace",
          "occurred_at": "2026-11-16T15:00:00Z",
          "data": {
            "transaction_id": "{{Guid.NewGuid():D}}",
            "merchant_id": "{{Guid.NewGuid():D}}",
            "type": "{{type}}",
            "amount": "{{amount}}",
            "currency": "{{currency}}",
            "reference_date": "2026-11-15",
            "created_at": "2026-11-16T15:00:00Z"
          }
        }
        """;

    [Fact]
    public void ValidEventMapsToProjection()
    {
        var parsed = TransactionRecordedEventParser.Parse(Encoding.UTF8.GetBytes(Payload()));
        Assert.Equal("CREDIT", parsed.Type);
        Assert.Equal(10.50m, parsed.Amount);
        Assert.Equal(new DateOnly(2026, 11, 15), parsed.ReferenceDate);
    }

    [Theory]
    [InlineData("CREDIT", "10.50", "USD", "1")]
    [InlineData("TRANSFER", "10.50", "BRL", "1")]
    [InlineData("CREDIT", "0.00", "BRL", "1")]
    [InlineData("CREDIT", "10.501", "BRL", "1")]
    [InlineData("CREDIT", "10.50", "BRL", "2")]
    public void InvalidOrIncompatibleEventIsRejected(string type, string amount,
        string currency, string version)
    {
        Assert.Throws<FormatException>(() =>
            TransactionRecordedEventParser.Parse(Encoding.UTF8.GetBytes(Payload(type, amount, currency, version))));
    }
}
