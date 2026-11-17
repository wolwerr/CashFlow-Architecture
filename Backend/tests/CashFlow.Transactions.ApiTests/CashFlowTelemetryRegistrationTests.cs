using CashFlow.Security.Observability;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace CashFlow.Transactions.ApiTests;

public sealed class CashFlowTelemetryRegistrationTests
{
    [Fact]
    public void DisabledTelemetryDoesNotRegisterExporters()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["CashFlowTelemetry:Enabled"] = "false";
        var before = builder.Services.Count;

        var returned = builder.AddCashFlowTelemetry("test-transactions");

        Assert.Same(builder, returned);
        Assert.Equal(before, builder.Services.Count);
    }

    [Fact]
    public void EnabledTelemetryRegistersOpenTelemetryServices()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["CashFlowTelemetry:Enabled"] = "true";
        var before = builder.Services.Count;

        var returned = builder.AddCashFlowTelemetry("test-transactions");

        Assert.Same(builder, returned);
        Assert.True(builder.Services.Count > before);
    }

    [Fact]
    public void MissingSettingDefaultsToDisabled()
    {
        var builder = WebApplication.CreateBuilder();
        var before = builder.Services.Count;

        builder.AddCashFlowTelemetry("test-transactions");

        Assert.Equal(before, builder.Services.Count);
    }
}
