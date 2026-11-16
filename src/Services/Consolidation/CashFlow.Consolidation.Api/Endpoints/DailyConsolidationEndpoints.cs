using System.Globalization;
using System.Security.Claims;
using CashFlow.Consolidation.Application.Gateways;
using CashFlow.Consolidation.Application.UseCases.GetDailyConsolidation;
using CashFlow.Consolidation.Infrastructure.Gateways;

namespace CashFlow.Consolidation.Api.Endpoints;

public static class DailyConsolidationEndpoints
{
    public static IEndpointRouteBuilder MapDailyConsolidation(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/consolidation/daily", HandleAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> HandleAsync(Guid? merchant_id, DateOnly? date,
        HttpContext context, IConsolidationAuthorizationGateway authorization,
        GetDailyConsolidationUseCase query, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(context.User.FindFirstValue("sub"), out var actor) ||
            actor == Guid.Empty)
            return Results.Problem(statusCode: 401, title: "Invalid authenticated subject.");
        if (merchant_id is null || merchant_id == Guid.Empty || date is null)
            return Results.Problem(statusCode: 400, title: "merchant_id and date are required.");

        bool allowed;
        try
        {
            allowed = await authorization.CanReadAsync(actor, merchant_id.Value, cancellationToken);
        }
        catch (MerchantAuthorizationUnavailableException)
        {
            return Results.Problem(statusCode: 503, title: "Merchant authorization unavailable.");
        }
        if (!allowed)
            return Results.Problem(statusCode: 403, title: "Merchant permission denied.");

        try
        {
            var (credit, debit, closing) =
                await query.ExecuteAsync(merchant_id.Value, date.Value, cancellationToken);
            return Results.Ok(new
            {
                merchant_id = merchant_id.Value,
                reference_date = date.Value,
                currency = "BRL",
                credit_total = credit.ToString("F2", CultureInfo.InvariantCulture),
                debit_total = debit.ToString("F2", CultureInfo.InvariantCulture),
                net_amount = (credit - debit).ToString("F2", CultureInfo.InvariantCulture),
                closing_balance = closing.ToString("F2", CultureInfo.InvariantCulture)
            });
        }
        catch (Exception ex) when (ex is Microsoft.EntityFrameworkCore.DbUpdateException
            or Npgsql.NpgsqlException)
        {
            return Results.Problem(statusCode: 503, title: "Consolidation database unavailable.");
        }
    }
}
