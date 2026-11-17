using Xunit;
namespace CashFlow.Consolidation.UnitTests;
public sealed class PlaceholderTests
{
    [Fact] public void DomainAssemblyIsAvailable() => Assert.NotNull(typeof(CashFlow.Consolidation.Domain.AssemblyMarker));
}
