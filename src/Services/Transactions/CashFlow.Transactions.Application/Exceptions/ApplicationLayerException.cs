namespace CashFlow.Transactions.Application.Exceptions;

/// <summary>Base for use-case orchestration failures. Do not use for domain invariants or transport errors.</summary>
public abstract class ApplicationLayerException : Exception
{
    protected ApplicationLayerException(string message) : base(message) { }
    protected ApplicationLayerException(string message, Exception innerException) : base(message, innerException) { }
}
