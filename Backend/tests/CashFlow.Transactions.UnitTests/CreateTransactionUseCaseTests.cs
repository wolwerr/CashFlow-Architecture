using CashFlow.Transactions.Application.Exceptions;
using CashFlow.Transactions.Application.Gateways;
using CashFlow.Transactions.Application.UseCases.CreateTransaction;
using CashFlow.Transactions.Application.UseCases.CreateTransaction.Commands;
using CashFlow.Transactions.Application.UseCases.CreateTransaction.Results;
using CashFlow.Transactions.Domain.Enums;
using CashFlow.Transactions.Domain.Exceptions;
using CashFlow.Transactions.Domain.Models;
using Xunit;

namespace CashFlow.Transactions.UnitTests;

public sealed class CreateTransactionUseCaseTests
{
    private static readonly Guid Merchant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 16);
    private static CreateTransactionCommand Command(decimal amount = 10.25m, DateOnly? date = null,
        string key = "request-key", string correlation = "correlation-123") =>
        new(Merchant, Actor, TransactionType.Credit, amount, date ?? Today, key, correlation);

    [Fact]
    public async Task AllowedRequestCreatesAndRegistersOnce()
    {
        var auth = new FakeAuthorization();
        var store = new FakeRegistration();
        var result = await UseCase(auth, store).ExecuteAsync(Command());
        Assert.Equal(1, auth.Calls);
        Assert.Equal(1, store.Calls);
        Assert.Equal(Merchant, store.LastTransaction!.MerchantId);
        Assert.Equal(Actor, store.LastTransaction.ActorId);
        Assert.Equal(10.25m, result.Amount);
        Assert.Equal(Now, result.CreatedAtUtc);
        Assert.Equal("request-key", store.LastKey);
        Assert.Equal("correlation-123", store.LastCorrelation);
    }

    [Fact]
    public async Task DebitOf25PersistsAsDebitWithoutChangingItsSignOrAmount()
    {
        var auth = new FakeAuthorization();
        var store = new FakeRegistration();
        var command = new CreateTransactionCommand(
            Merchant, Actor, TransactionType.Debit, 25m, Today, "debit-key", "debit-correlation");
        var result = await UseCase(auth, store).ExecuteAsync(command);

        Assert.Equal(TransactionType.Debit, result.Type);
        Assert.Equal(25m, result.Amount);
        Assert.Equal(TransactionType.Debit, store.LastTransaction!.Type);
        Assert.Equal(25m, store.LastTransaction.Amount);
        Assert.Equal(1, auth.Calls);
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task UnauthorizedDoesNotPersist()
    {
        var auth = new FakeAuthorization { Allowed = false };
        var store = new FakeRegistration();
        await Assert.ThrowsAsync<TransactionAuthorizationException>(() => UseCase(auth, store).ExecuteAsync(Command()));
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task AuthorizationFailureDoesNotPersist()
    {
        var auth = new FakeAuthorization { Failure = new InvalidOperationException("authorization unavailable") };
        var store = new FakeRegistration();
        await Assert.ThrowsAsync<InvalidOperationException>(() => UseCase(auth, store).ExecuteAsync(Command()));
        Assert.Equal(0, store.Calls);
    }

    [Theory]
    [InlineData("")] [InlineData("  ")]
    public async Task MissingIdempotencyKeyIsRejected(string key)
    {
        var auth = new FakeAuthorization();
        var store = new FakeRegistration();
        await Assert.ThrowsAsync<InvalidIdempotencyKeyException>(() => UseCase(auth, store).ExecuteAsync(Command(key: key)));
        Assert.Equal(0, auth.Calls);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task LongIdempotencyKeyIsRejected()
    {
        var store = new FakeRegistration();
        await Assert.ThrowsAsync<InvalidIdempotencyKeyException>(() => UseCase(new FakeAuthorization(), store).ExecuteAsync(Command(key: new string('a', 129))));
        Assert.Equal(0, store.Calls);
    }

    [Theory]
    [InlineData("")] [InlineData(" ")]
    public async Task MissingCorrelationIdIsRejected(string correlation)
    {
        var store = new FakeRegistration();
        await Assert.ThrowsAsync<InvalidCorrelationIdException>(() => UseCase(new FakeAuthorization(), store).ExecuteAsync(Command(correlation: correlation)));
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task LongCorrelationIdIsRejected()
    {
        var store = new FakeRegistration();
        await Assert.ThrowsAsync<InvalidCorrelationIdException>(() => UseCase(new FakeAuthorization(), store).ExecuteAsync(Command(correlation: new string('a', 129))));
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task FutureReferenceDateDoesNotPersist()
    {
        var store = new FakeRegistration();
        await Assert.ThrowsAsync<FutureReferenceDateException>(() => UseCase(new FakeAuthorization(), store).ExecuteAsync(Command(date: Today.AddDays(1))));
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task InvalidAmountDoesNotPersist()
    {
        var store = new FakeRegistration();
        await Assert.ThrowsAsync<InvalidTransactionAmountException>(() => UseCase(new FakeAuthorization(), store).ExecuteAsync(Command(amount: 0m)));
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task OldReferenceDateIsPermitted()
    {
        var result = await UseCase(new FakeAuthorization(), new FakeRegistration()).ExecuteAsync(Command(date: new DateOnly(1900, 1, 1)));
        Assert.Equal(new DateOnly(1900, 1, 1), result.ReferenceDate);
    }

    [Fact]
    public async Task PersistenceFailureIsNotReturnedAsSuccess()
    {
        var store = new FakeRegistration { Failure = new TransactionIdempotencyConflictException() };
        await Assert.ThrowsAsync<TransactionIdempotencyConflictException>(() => UseCase(new FakeAuthorization(), store).ExecuteAsync(Command()));
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task NullCommandIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => UseCase(new FakeAuthorization(), new FakeRegistration()).ExecuteAsync(null!));
    }

    private static CreateTransactionUseCase UseCase(FakeAuthorization auth, FakeRegistration store) =>
        new(auth, store, new FixedTimeProvider());

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeAuthorization : ICreateTransactionAuthorizationGateway
    {
        public bool Allowed { get; init; } = true;
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }
        public Task<bool> CanCreateAsync(Guid actorId, Guid merchantId, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(Actor, actorId);
            Assert.Equal(Merchant, merchantId);
            if (Failure is not null) throw Failure;
            return Task.FromResult(Allowed);
        }
    }

    private sealed class FakeRegistration : ITransactionRegistrationGateway
    {
        public int Calls { get; private set; }
        public Transaction? LastTransaction { get; private set; }
        public string? LastKey { get; private set; }
        public string? LastCorrelation { get; private set; }
        public Exception? Failure { get; init; }
        public Task<CreateTransactionResult> RegisterAsync(Transaction transaction, string idempotencyKey,
            string correlationId, CancellationToken cancellationToken)
        {
            Calls++;
            LastTransaction = transaction;
            LastKey = idempotencyKey;
            LastCorrelation = correlationId;
            if (Failure is not null) throw Failure;
            return Task.FromResult(new CreateTransactionResult(transaction.Id, transaction.MerchantId,
                transaction.Type, transaction.Amount, transaction.ReferenceDate, transaction.CreatedAtUtc));
        }
    }
}
