namespace CashFlow.Transactions.Infrastructure.DTOs;

/// <summary>Published event contract v1. Values use invariant, wire-compatible JSON formatting.</summary>
public sealed record TransactionRecordedDataDto(
    Guid TransactionId,
    Guid MerchantId,
    string Type,
    string Amount,
    string Currency,
    DateOnly ReferenceDate,
    DateTimeOffset CreatedAt);

public sealed record TransactionRecordedDto(
    Guid EventId,
    string EventType,
    string SchemaVersion,
    string CorrelationId,
    DateTimeOffset OccurredAt,
    TransactionRecordedDataDto Data);
