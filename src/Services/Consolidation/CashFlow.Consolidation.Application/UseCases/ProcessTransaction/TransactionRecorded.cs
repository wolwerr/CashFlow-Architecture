namespace CashFlow.Consolidation.Application.UseCases.ProcessTransaction;

/// <summary>Stable projection input after validating the versioned event envelope.</summary>
public sealed record TransactionRecorded(
    Guid EventId, Guid TransactionId, Guid MerchantId, string Type,
    decimal Amount, DateOnly ReferenceDate, DateTimeOffset CreatedAtUtc);
