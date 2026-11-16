using CashFlow.Transactions.Domain;
using Xunit;

namespace CashFlow.Transactions.UnitTests;

public sealed class TransactionTests
{
    private static readonly Guid Merchant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 11, 15);
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static Transaction Create(TransactionType type = TransactionType.Credit,
        decimal amount = 10.50m, DateOnly? date = null) =>
        Transaction.Create(Merchant, Actor, type, amount, date ?? Today, Today, Now);

    [Fact] public void CreditHasPositiveSignedAmount() => Assert.Equal(10.50m, Create().SignedAmount);
    [Fact] public void DebitHasNegativeSignedAmount() => Assert.Equal(-10.50m, Create(TransactionType.Debit).SignedAmount);
    [Fact] public void RecordsGivenIdentifiersAndUtcTimestamp()
    {
        var result = Create();
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(Merchant, result.MerchantId);
        Assert.Equal(Actor, result.ActorId);
        Assert.Equal(Today, result.ReferenceDate);
        Assert.Equal(Now, result.CreatedAtUtc);
    }
    [Fact] public void PastDateHasNoRetroactiveLimit() => Assert.Equal(new DateOnly(1900, 1, 1), Create(date: new DateOnly(1900, 1, 1)).ReferenceDate);
    [Fact] public void FutureDateIsRejected() => Assert.Throws<ArgumentOutOfRangeException>(() => Create(date: Today.AddDays(1)));
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(0.001)] [InlineData(1.999)]
    public void InvalidAmountsAreRejected(decimal amount) => Assert.Throws<ArgumentOutOfRangeException>(() => Create(amount: amount));
    [Theory]
    [InlineData(0.01)] [InlineData(1000000.99)]
    public void ValidAmountsAreAccepted(decimal amount) => Assert.Equal(amount, Create(amount: amount).Amount);
    [Fact] public void InvalidTypeIsRejected() => Assert.Throws<ArgumentOutOfRangeException>(() => Create((TransactionType)999));
    [Fact] public void EmptyMerchantIsRejected() => Assert.Throws<ArgumentException>(() => Transaction.Create(Guid.Empty, Actor, TransactionType.Credit, 1m, Today, Today, Now));
    [Fact] public void EmptyActorIsRejected() => Assert.Throws<ArgumentException>(() => Transaction.Create(Merchant, Guid.Empty, TransactionType.Credit, 1m, Today, Today, Now));
    [Fact] public void NonUtcTimestampIsRejected() => Assert.Throws<ArgumentException>(() => Transaction.Create(Merchant, Actor, TransactionType.Credit, 1m, Today, Today, Now.ToOffset(TimeSpan.FromHours(-3))));
    [Fact] public void SaoPauloCalendarUsesLocalDayRatherThanUtcDay()
    {
        var utc = new DateTimeOffset(2026, 11, 16, 1, 30, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 11, 15), BusinessCalendar.CurrentDate(utc));
    }
    [Fact] public void SaoPauloCalendarRejectsNonUtcInstant() => Assert.Throws<ArgumentException>(() => BusinessCalendar.CurrentDate(Now.ToOffset(TimeSpan.FromHours(-3))));
}
