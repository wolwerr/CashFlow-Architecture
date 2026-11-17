using CashFlow.Consolidation.Domain.Models;
using Xunit;

namespace CashFlow.Consolidation.UnitTests;

/// <summary>
/// Regressions for the local E2E case where a debit reached the transaction
/// history before the asynchronous daily projection reflected the debit.
/// </summary>
public sealed class FinancialOperationRegressionTests
{
    [Fact]
    public void CreditOf100AndDebitOf25ProducesClosingBalanceOf75()
    {
        var balance = new DailyBalance(Guid.NewGuid(), new DateOnly(2026, 11, 16));
        balance.Apply("CREDIT", 100m);
        balance.Apply("DEBIT", 25m);

        Assert.Equal(100m, balance.CreditTotal);
        Assert.Equal(25m, balance.DebitTotal);
        Assert.Equal(75m, balance.NetAmount);
    }

    [Fact]
    public void DebitAndCreditOrderDoesNotChangeFinancialResult()
    {
        var balance = new DailyBalance(Guid.NewGuid(), new DateOnly(2026, 11, 16));
        balance.Apply("DEBIT", 25m);
        balance.Apply("CREDIT", 100m);

        Assert.Equal(100m, balance.CreditTotal);
        Assert.Equal(25m, balance.DebitTotal);
        Assert.Equal(75m, balance.NetAmount);
    }

    [Fact]
    public void AdditionalDebitReducesNetButNeverCreditTotal()
    {
        var balance = new DailyBalance(Guid.NewGuid(), new DateOnly(2026, 11, 16));
        balance.Apply("CREDIT", 100m);
        balance.Apply("DEBIT", 25m);
        balance.Apply("DEBIT", 10m);

        Assert.Equal(100m, balance.CreditTotal);
        Assert.Equal(35m, balance.DebitTotal);
        Assert.Equal(65m, balance.NetAmount);
    }
}
