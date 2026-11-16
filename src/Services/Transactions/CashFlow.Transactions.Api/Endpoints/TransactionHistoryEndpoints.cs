using System.Globalization;
using System.Security.Claims;
using CashFlow.Transactions.Application.Gateways;
using CashFlow.Transactions.Infrastructure.Entities;
using CashFlow.Transactions.Infrastructure.Persistence;
using CashFlow.Security.Keycloak;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Transactions.Api.Endpoints;

/// <summary>
/// Read-only paginated merchant-scoped history. Always authorize the requested merchant
/// before any database operation, and constrain the SQL query by the same merchant UUID.
/// </summary>
public static class TransactionHistoryEndpoints
{
    public static IEndpointRouteBuilder MapTransactionHistory(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/transactions", HandleAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> HandleAsync(Guid? merchant_id, int? page, int? page_size,
        HttpContext context, ICreateTransactionAuthorizationGateway authorization,
        TransactionsDbContext db, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(context.User.FindFirstValue("sub"), out var actorId) || actorId == Guid.Empty)
            return Results.Problem(statusCode: 401, title: "Invalid authenticated subject.");

        if (merchant_id is null || merchant_id == Guid.Empty ||
            page is < 1 or > 100000 || page_size is < 1 or > 100)
            return Results.Problem(statusCode: 400, title: "Invalid merchant or pagination.");

        var merchant = merchant_id.Value;
        var pageNumber = page ?? 1;
        var pageSize = page_size ?? 20;
        bool allowed;
        try
        {
            allowed = await authorization.CanCreateAsync(actorId, merchant, cancellationToken);
        }
        catch (MerchantAuthorizationUnavailableException)
        {
            return Results.Problem(statusCode: 503, title: "Merchant authorization unavailable.");
        }
        if (!allowed)
            return Results.Problem(statusCode: 403, title: "Merchant permission denied.");

        try
        {
            var rows = await db.Transactions.AsNoTracking()
                .Where(x => x.MerchantId == merchant)
                .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize + 1)
                .ToListAsync(cancellationToken);
            var items = rows.Take(pageSize).Select(ToItem).ToArray();
            return Results.Ok(new
            {
                items,
                page = pageNumber,
                page_size = pageSize,
                has_more = rows.Count > pageSize
            });
        }
        catch (Exception ex) when (ex is DbUpdateException or Npgsql.NpgsqlException)
        {
            return Results.Problem(statusCode: 503, title: "Transaction history unavailable.");
        }
    }

    private static object ToItem(TransactionEntity row) => new
    {
        id = row.Id,
        merchant_id = row.MerchantId,
        type = row.Type == CashFlow.Transactions.Domain.Enums.TransactionType.Credit
            ? "CREDIT" : "DEBIT",
        amount = row.Amount.ToString("F2", CultureInfo.InvariantCulture),
        currency = "BRL",
        reference_date = row.ReferenceDate,
        created_at = row.CreatedAtUtc
    };
}
