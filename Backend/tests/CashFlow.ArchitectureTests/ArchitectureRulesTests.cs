using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;
using Xunit;

namespace CashFlow.ArchitectureTests;

public sealed class ArchitectureRulesTests
{
    private static readonly string[] Services = [ "Transactions", "Consolidation" ];
    private static readonly string[] Layers = [ "Domain", "Application", "Infrastructure", "Api" ];

    [Theory]
    [InlineData("Transactions", "Domain")]
    [InlineData("Transactions", "Application")]
    [InlineData("Consolidation", "Domain")]
    [InlineData("Consolidation", "Application")]
    public void InnerLayersMayNotReferenceThirdPartyLibraries(string service, string layer)
    {
        var assembly = Load(service, layer);
        var permitted = new[] { "System", "netstandard", "mscorlib", "CashFlow." };
        var forbidden = assembly.GetReferencedAssemblies()
            .Select(x => x.Name!)
            .Where(name => !permitted.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();
        Assert.True(forbidden.Length == 0, $"{service}.{layer} contains forbidden assembly references: {string.Join(", ", forbidden)}");
    }

    [Theory]
    [InlineData("Transactions", "Domain")]
    [InlineData("Transactions", "Application")]
    [InlineData("Consolidation", "Domain")]
    [InlineData("Consolidation", "Application")]
    public void InnerLayersMustNotHaveNuGetPackageReferences(string service, string layer)
    {
        var path = Path.Combine(FindRepoRoot(), "src", "Services", service, $"CashFlow.{service}.{layer}", $"CashFlow.{service}.{layer}.csproj");
        var document = XDocument.Load(path);
        var references = document.Descendants().Where(x => x.Name.LocalName == "PackageReference").Select(x => (string?)x.Attribute("Include")).ToArray();
        Assert.True(references.Length == 0, $"{service}.{layer} must not contain NuGet references: {string.Join(", ", references)}");
    }

    [Theory]
    [InlineData("Transactions", "Domain", "")]
    [InlineData("Transactions", "Application", "Domain")]
    [InlineData("Consolidation", "Domain", "")]
    [InlineData("Consolidation", "Application", "Domain")]
    public void InnerProjectReferencesAreRestricted(string service, string layer, string allowedLayer)
    {
        var path = Path.Combine(FindRepoRoot(), "src", "Services", service, $"CashFlow.{service}.{layer}", $"CashFlow.{service}.{layer}.csproj");
        var doc = XDocument.Load(path);
        var actual = doc.Descendants().Where(x => x.Name.LocalName == "ProjectReference")
            .Select(x => Path.GetFileNameWithoutExtension((string?)x.Attribute("Include"))).ToArray();
        var allowed = allowedLayer.Length == 0 ? Array.Empty<string>() : [$"CashFlow.{service}.{allowedLayer}"];
        Assert.Equal(allowed, actual);
    }

    [Theory]
    [InlineData("Transactions")]
    [InlineData("Consolidation")]
    public void FinancialServicesMustNotReferenceOtherFinancialService(string service)
    {
        var other = service == "Transactions" ? "Consolidation" : "Transactions";
        foreach (var layer in Layers)
        {
            var asm = Load(service, layer);
            Assert.DoesNotContain(asm.GetReferencedAssemblies(), x => x.Name!.StartsWith($"CashFlow.{other}.", StringComparison.Ordinal));
            var result = Types.InAssembly(asm).ShouldNot().HaveDependencyOn($"CashFlow.{other}").GetResult();
            Assert.True(result.IsSuccessful, $"{service}.{layer} has a forbidden dependency on {other}");
        }
    }

    [Theory]
    [InlineData("Transactions")]
    [InlineData("Consolidation")]
    public void LayersMustNotDependOnOuterLayers(string service)
    {
        var domain = Load(service, "Domain");
        var application = Load(service, "Application");
        var infrastructure = Load(service, "Infrastructure");
        foreach (var forbidden in new[] { "Application", "Infrastructure", "Api" })
            Assert.True(Types.InAssembly(domain).ShouldNot().HaveDependencyOn($"CashFlow.{service}.{forbidden}").GetResult().IsSuccessful);
        foreach (var forbidden in new[] { "Infrastructure", "Api" })
            Assert.True(Types.InAssembly(application).ShouldNot().HaveDependencyOn($"CashFlow.{service}.{forbidden}").GetResult().IsSuccessful);
        Assert.True(Types.InAssembly(infrastructure).ShouldNot().HaveDependencyOn($"CashFlow.{service}.Api").GetResult().IsSuccessful);
    }

    private static Assembly Load(string service, string layer) => Assembly.Load($"CashFlow.{service}.{layer}");

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CashFlow.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("CashFlow.slnx was not found.");
    }
}
