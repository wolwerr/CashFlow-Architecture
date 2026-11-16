using System.Text.Json.Serialization;

namespace CashFlow.Transactions.Api.DTOs;

public sealed record CreateTransactionRequest(
    [property: JsonPropertyName("merchant_id")] Guid MerchantId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("amount")] string Amount,
    [property: JsonPropertyName("reference_date")] DateOnly ReferenceDate);

public sealed record TransactionResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("merchant_id")] Guid MerchantId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("amount")] string Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("reference_date")] DateOnly ReferenceDate,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);
