using System.Globalization;
using System.Security.Claims;
using CashFlow.Transactions.Api.DTOs;
using CashFlow.Transactions.Application.Exceptions;
using CashFlow.Transactions.Application.UseCases.CreateTransaction;
using CashFlow.Transactions.Application.UseCases.CreateTransaction.Commands;
using CashFlow.Transactions.Domain.Enums;
using CashFlow.Transactions.Domain.Exceptions;
using CashFlow.Transactions.Infrastructure.Gateways;
using CashFlow.Transactions.Infrastructure.Exceptions;

namespace CashFlow.Transactions.Api.Endpoints;

public static class CreateTransactionEndpoints
{
    public static IEndpointRouteBuilder MapCreateTransaction(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/transactions", async (
            CreateTransactionRequest? request,
            HttpContext context, CreateTransactionUseCase useCase, CancellationToken ct) =>
        {
            if (request is null)
                return Results.Problem(statusCode: 400, title: "Request body required.");

            var subject = context.User.FindFirstValue("sub");
            if (!Guid.TryParse(subject, out var actorId) || actorId == Guid.Empty)
                return Results.Problem(statusCode: 401, title: "Invalid authenticated subject.");

            if (request.MerchantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "merchant_id is required.");

            if (!Enum.TryParse<TransactionType>(request.Type, true, out var type) ||
                !Enum.IsDefined(type) ||
                request.Type is null ||
                !(request.Type.Equals("CREDIT", StringComparison.OrdinalIgnoreCase) ||
                  request.Type.Equals("DEBIT", StringComparison.OrdinalIgnoreCase)))
                return Results.Problem(statusCode: 400, title: "Invalid transaction type.");

            if (request.Amount is null ||
                !System.Text.RegularExpressions.Regex.IsMatch(request.Amount,
                    @"^[0-9]+\.[0-9]{2}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) ||
                !decimal.TryParse(request.Amount, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var amount) || amount <= 0 ||
                amount > 99999999999999999.99m)
                return Results.Problem(statusCode: 422, title: "Invalid BRL amount.");

            var key = context.Request.Headers["Idempotency-Key"].ToString();
            var correlation = context.Request.Headers["X-Correlation-ID"].ToString();
            if (string.IsNullOrWhiteSpace(correlation))
                correlation = Guid.NewGuid().ToString("D");

            try
            {
                var result = await useCase.ExecuteAsync(new CreateTransactionCommand(
                    request.MerchantId, actorId, type, amount, request.ReferenceDate, key, correlation), ct);
                context.Response.Headers["X-Correlation-ID"] = correlation;
                context.Response.Headers.Location = $"/api/v1/transactions/{result.Id:D}";
                var response = new TransactionResponse(result.Id, result.MerchantId,
                    result.Type == TransactionType.Credit ? "CREDIT" : "DEBIT",
                    result.Amount.ToString("F2", CultureInfo.InvariantCulture),
                    "BRL", result.ReferenceDate, result.CreatedAtUtc);
                return Results.Json(response, statusCode: StatusCodes.Status201Created);
            }
            catch (InvalidIdempotencyKeyException)
            {
                return Results.Problem(statusCode: 400, title: "Invalid Idempotency-Key.");
            }
            catch (InvalidCorrelationIdException)
            {
                return Results.Problem(statusCode: 400, title: "Invalid X-Correlation-ID.");
            }
            catch (TransactionAuthorizationException)
            {
                return Results.Problem(statusCode: 403, title: "Merchant permission denied.");
            }
            catch (MerchantAuthorizationUnavailableException)
            {
                return Results.Problem(statusCode: 503, title: "Merchant authorization unavailable.");
            }
            catch (TransactionIdempotencyConflictException)
            {
                return Results.Problem(statusCode: 409, title: "Idempotency-Key conflict.");
            }
            catch (DomainException)
            {
                return Results.Problem(statusCode: 422, title: "Invalid transaction.");
            }
            catch (TransactionPersistenceException)
            {
                return Results.Problem(statusCode: 503, title: "Transaction storage unavailable.");
            }
        }).RequireAuthorization();
        return app;
    }
}
