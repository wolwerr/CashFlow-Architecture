namespace CashFlow.Transactions.Infrastructure.Messaging;

public sealed class RabbitMqConnectionSettings
{
    public const string SectionName = "RabbitMq";
    public string Host { get; set; } = "rabbitmq";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Exchange { get; set; } = "cashflow.transactions";
    public string Queue { get; set; } = "cashflow.consolidation.transaction-recorded";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host) || Port is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(User) || string.IsNullOrWhiteSpace(Password) ||
            string.IsNullOrWhiteSpace(Exchange) || string.IsNullOrWhiteSpace(Queue))
            throw new InvalidOperationException("RabbitMq settings must be configured.");
    }
}
