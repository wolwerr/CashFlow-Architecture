using System.Globalization;
using System.Text.Json;
using CashFlow.Consolidation.Application.UseCases.ProcessTransaction;

namespace CashFlow.Consolidation.Infrastructure.Messaging;

/// <summary>Rejects malformed and unsupported events before they touch the projection DB.</summary>
public static class TransactionRecordedEventParser
{
    public static TransactionRecorded Parse(ReadOnlySpan<byte> body)
    {
        using var json = JsonDocument.Parse(body.ToArray());
        var root = json.RootElement;
        if (root.GetProperty("event_type").GetString() != "transaction.recorded" ||
            root.GetProperty("schema_version").GetString() != "1")
            throw new FormatException("Unsupported event contract.");
        var eventId = root.GetProperty("event_id").GetGuid();
        var data = root.GetProperty("data");
        var transactionId = data.GetProperty("transaction_id").GetGuid();
        var merchantId = data.GetProperty("merchant_id").GetGuid();
        var type = data.GetProperty("type").GetString();
        var currency = data.GetProperty("currency").GetString();
        var amountText = data.GetProperty("amount").GetString();
        var referenceDate = DateOnly.ParseExact(
            data.GetProperty("reference_date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var createdAt = data.GetProperty("created_at").GetDateTimeOffset();
        if (currency != "BRL" || type is not ("CREDIT" or "DEBIT") ||
            !decimal.TryParse(amountText, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0 || amount != decimal.Round(amount, 2) ||
            eventId == Guid.Empty || transactionId == Guid.Empty || merchantId == Guid.Empty ||
            createdAt.Offset != TimeSpan.Zero)
            throw new FormatException("Invalid transaction event fields.");
        return new TransactionRecorded(eventId, transactionId, merchantId, type, amount,
            referenceDate, createdAt);
    }
}
