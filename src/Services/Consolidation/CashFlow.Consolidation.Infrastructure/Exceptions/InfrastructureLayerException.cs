namespace CashFlow.Consolidation.Infrastructure.Exceptions;

/// <summary>Base for persistence, messaging and adapter failures. Never disclose sensitive details in API responses.</summary>
public abstract class InfrastructureLayerException : Exception
{
    protected InfrastructureLayerException(string message) : base(message) { }
    protected InfrastructureLayerException(string message, Exception innerException) : base(message, innerException) { }
}
