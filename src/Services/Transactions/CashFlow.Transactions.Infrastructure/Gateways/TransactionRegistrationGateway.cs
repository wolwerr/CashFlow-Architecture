using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CashFlow.Transactions.Application.Exceptions;
using CashFlow.Transactions.Application.Gateways;
using CashFlow.Transactions.Application.UseCases.CreateTransaction.Results;
using CashFlow.Transactions.Domain.Enums;
using CashFlow.Transactions.Domain.Models;
using CashFlow.Transactions.Infrastructure.DTOs;
using CashFlow.Transactions.Infrastructure.Entities;
using CashFlow.Transactions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CashFlow.Transactions.Infrastructure.Gateways;

/// <summary>
/// Persists a registration and its event, audit and idempotency key in ONE PostgreSQL commit.
/// The unique (merchant_id, request_key) constraint serializes competing requests for that key.
/// No message is published here; a later outbox publisher will handle delivery.
/// </summary>
public sealed class TransactionRegistrationGateway(TransactionsDbContext db)
    : ITransactionRegistrationGateway
{
    private static readonly JsonSerializerOptions EventJsonOptions =
        new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task<CreateTransactionResult> RegisterAsync(
        Transaction transaction, string idempotencyKey, string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var requestHash = RequestHash(transaction);
        // A key reserved by another concurrent request is resolved by PostgreSQL's
        // unique index. The second insert waits for the first transaction to finish.
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            // Reserve first. On conflict, no other records from this request are saved.
            db.IdempotencyRecords.Add(new IdempotencyRecordEntity
            {
                MerchantId = transaction.MerchantId,
                RequestKey = idempotencyKey,
                RequestHash = requestHash,
                TransactionId = transaction.Id,
                CreatedAtUtc = transaction.CreatedAtUtc
            });
            await db.SaveChangesAsync(cancellationToken);

            var eventId = Guid.NewGuid();
            var payload = new TransactionRecordedDto(
                eventId, "transaction.recorded", "1", correlationId, transaction.CreatedAtUtc,
                new TransactionRecordedDataDto(
                    transaction.Id, transaction.MerchantId,
                    transaction.Type == TransactionType.Credit ? "CREDIT" : "DEBIT",
                    transaction.Amount.ToString("F2", CultureInfo.InvariantCulture),
                    "BRL", transaction.ReferenceDate, transaction.CreatedAtUtc));

            db.Transactions.Add(new TransactionEntity
            {
                Id = transaction.Id,
                MerchantId = transaction.MerchantId,
                ActorId = transaction.ActorId,
                Type = transaction.Type,
                Amount = transaction.Amount,
                ReferenceDate = transaction.ReferenceDate,
                CreatedAtUtc = transaction.CreatedAtUtc
            });
            db.OutboxMessages.Add(new OutboxMessageEntity
            {
                EventId = eventId,
                MerchantId = transaction.MerchantId,
                TransactionId = transaction.Id,
                EventType = "transaction.recorded",
                SchemaVersion = "1",
                CorrelationId = correlationId,
                Payload = JsonSerializer.Serialize(payload, EventJsonOptions),
                CreatedAtUtc = transaction.CreatedAtUtc,
                AttemptCount = 0
            });
            db.TransactionAudits.Add(new TransactionAuditEntity
            {
                Id = Guid.NewGuid(),
                TransactionId = transaction.Id,
                MerchantId = transaction.MerchantId,
                ActorId = transaction.ActorId,
                Action = "CREATED",
                CorrelationId = correlationId,
                OccurredAtUtc = transaction.CreatedAtUtc
            });
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return ToResult(transaction);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "PK_idempotency_records"
            })
        {
            // The transaction scope has been disposed/rolled back before querying.
            // Never trust the uncommitted entity in this DbContext.
            db.ChangeTracker.Clear();
            var previous = await db.IdempotencyRecords.AsNoTracking()
                .SingleOrDefaultAsync(x => x.MerchantId == transaction.MerchantId
                    && x.RequestKey == idempotencyKey, cancellationToken);

            if (previous is null)
                throw new InvalidOperationException(
                    "Idempotency conflict was detected but no committed record could be loaded.", ex);

            if (!string.Equals(previous.RequestHash, requestHash, StringComparison.Ordinal))
                throw new TransactionIdempotencyConflictException();

            var stored = await db.Transactions.AsNoTracking().SingleOrDefaultAsync(
                x => x.MerchantId == transaction.MerchantId
                    && x.Id == previous.TransactionId, cancellationToken);

            if (stored is null)
                throw new InvalidOperationException(
                    "Idempotency record exists without its committed transaction.", ex);

            return new CreateTransactionResult(
                stored.Id, stored.MerchantId, stored.Type, stored.Amount,
                stored.ReferenceDate, stored.CreatedAtUtc);
        }
    }

    private static CreateTransactionResult ToResult(Transaction transaction) =>
        new(transaction.Id, transaction.MerchantId, transaction.Type,
            transaction.Amount, transaction.ReferenceDate, transaction.CreatedAtUtc);

    private static string RequestHash(Transaction transaction)
    {
        // Actor participates in the fingerprint to prevent returning another actor's
        // result for the same merchant/key. The correlation ID is deliberately excluded:
        // retries can have different traces but must preserve the business request.
        var payload = string.Join("|",
            transaction.MerchantId.ToString("D"),
            transaction.ActorId.ToString("D"),
            ((int)transaction.Type).ToString(CultureInfo.InvariantCulture),
            transaction.Amount.ToString("F2", CultureInfo.InvariantCulture),
            transaction.ReferenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
