using CashFlow.Consolidation.Application.Gateways;
using CashFlow.Consolidation.Application.UseCases.GetDailyConsolidation;
using CashFlow.Consolidation.Application.UseCases.ProcessTransaction;
using CashFlow.Consolidation.Domain.Models;
using Xunit;

namespace CashFlow.Consolidation.UnitTests;

public sealed class ConsolidationDomainAndUseCaseTests
{
    [Fact]
    public void CreditAndDebitAccumulateWithSignedNetOnly()
    {
        var balance = new DailyBalance(Guid.NewGuid(), new DateOnly(2026, 11, 1));
        balance.Apply("CREDIT", 110.25m);
        balance.Apply("DEBIT", 30.10m);
        Assert.Equal(110.25m, balance.CreditTotal);
        Assert.Equal(30.10m, balance.DebitTotal);
        Assert.Equal(80.15m, balance.NetAmount);
    }

    [Fact]
    public void InvalidTransactionOrMerchantIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new DailyBalance(Guid.Empty, new DateOnly(2026, 11, 1)));
        var balance = new DailyBalance(Guid.NewGuid(), new DateOnly(2026, 11, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => balance.Apply("CREDIT", 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => balance.Apply("DEBIT", 1.001m));
        Assert.Throws<ArgumentException>(() => balance.Apply("TRANSFER", 1m));
    }

    [Fact]
    public async Task ProcessCaseDelegatesValidMessageOnce()
    {
        var fake = new FakeGateway();
        var transaction = Event();
        Assert.True(await new ProcessTransactionUseCase(fake).ExecuteAsync(transaction, default));
        Assert.Same(transaction, fake.Last);
    }

    [Theory]
    [InlineData("TRANSFER", 10)]
    [InlineData("DEBIT", 0)]
    [InlineData("CREDIT", -1)]
    [InlineData("CREDIT", 1.001)]
    public async Task ProcessRejectsInvalidPayload(string type, decimal amount)
    {
        var valid = Event();
        var bad = valid with { Type = type, Amount = amount };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new ProcessTransactionUseCase(new FakeGateway()).ExecuteAsync(bad, default));
    }

    [Fact]
    public async Task DailyQueryRequiresMerchantAndDelegates()
    {
        var merchant = Guid.NewGuid();
        var date = new DateOnly(2026, 11, 1);
        var gateway = new FakeGateway();
        var result = await new GetDailyConsolidationUseCase(gateway).ExecuteAsync(merchant, date, default);
        Assert.Equal(25m, result.Closing);
        Assert.Equal(merchant, gateway.LastQueryMerchant);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new GetDailyConsolidationUseCase(gateway).ExecuteAsync(Guid.Empty, date, default));
    }

    private static TransactionRecorded Event() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "CREDIT", 10m,
        new DateOnly(2026, 11, 1), new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero));

    private sealed class FakeGateway : IConsolidationGateway
    {
        public TransactionRecorded? Last { get; private set; }
        public Guid LastQueryMerchant { get; private set; }
        public Task<bool> ApplyAsync(TransactionRecorded message, CancellationToken cancellationToken)
        {
            Last = message;
            return Task.FromResult(true);
        }

        public Task<(decimal Credit, decimal Debit, decimal Closing)> GetDailyAsync(
            Guid merchantId, DateOnly referenceDate, CancellationToken cancellationToken)
        {
            LastQueryMerchant = merchantId;
            return Task.FromResult((30m, 5m, 25m));
        }
    }
}
