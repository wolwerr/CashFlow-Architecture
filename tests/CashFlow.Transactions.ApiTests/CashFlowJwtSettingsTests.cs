using CashFlow.Security.Authentication;
using Xunit;

namespace CashFlow.Transactions.ApiTests;

public sealed class CashFlowJwtSettingsTests
{
    private static CashFlowJwtSettings Valid() => new()
    {
        Authority = "http://localhost:8081/realms/cashflow",
        Audience = "cashflow-transactions-api"
    };

    [Fact]
    public void ValidDevelopmentSettingsPass() => Valid().Validate(true);

    [Fact]
    public void ProductionRequiresHttpsIssuer()
    {
        Assert.Throws<InvalidOperationException>(() => Valid().Validate(false));
    }

    [Fact]
    public void ProductionAcceptsHttpsIssuerAndMetadata()
    {
        new CashFlowJwtSettings
        {
            Authority = "https://identity.example.test/realms/cashflow",
            Audience = "cashflow-transactions-api",
            MetadataAddress = "https://identity.example.test/realms/cashflow/.well-known/openid-configuration"
        }.Validate(false);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://localhost:8081/not-a-realm")]
    [InlineData("http://localhost:8081/realms/cashflow?evil=true")]
    public void InvalidIssuerFailsClosed(string authority)
    {
        var settings = new CashFlowJwtSettings { Authority = authority, Audience = "cashflow-transactions-api" };
        Assert.Throws<InvalidOperationException>(() => settings.Validate(true));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("api audience")]
    public void InvalidAudienceFailsClosed(string audience)
    {
        var settings = new CashFlowJwtSettings { Authority = Valid().Authority, Audience = audience };
        Assert.Throws<InvalidOperationException>(() => settings.Validate(true));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("http://localhost:8081/realms/cashflow/token")]
    public void InvalidMetadataAddressFailsClosed(string address)
    {
        var settings = new CashFlowJwtSettings
        {
            Authority = Valid().Authority, Audience = Valid().Audience, MetadataAddress = address
        };
        Assert.Throws<InvalidOperationException>(() => settings.Validate(true));
    }

    [Fact]
    public void ProductionRejectsHttpMetadataEvenWithHttpsAuthority()
    {
        var settings = new CashFlowJwtSettings
        {
            Authority = "https://identity.example.test/realms/cashflow",
            Audience = "cashflow-transactions-api",
            MetadataAddress = "http://identity.example.test/realms/cashflow/.well-known/openid-configuration"
        };
        Assert.Throws<InvalidOperationException>(() => settings.Validate(false));
    }
}
