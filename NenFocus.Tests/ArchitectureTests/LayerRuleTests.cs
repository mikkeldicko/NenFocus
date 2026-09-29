using System.Reflection;
using System.Text.RegularExpressions;
using NenFocus.Application.Services;
using NenFocus.Domain.Services;
using NenFocus.Infrastructure.Services;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// Dependencies only flow inward: UI → Application ← Infrastructure → Domain
/// (ARCHITECT_GOVERNANCE.md → Architecture; MODULE_STANDARDS → Layer Responsibilities).
/// </summary>
public class LayerRuleTests
{
    private static readonly Dictionary<Assembly, string[]> AllowedReferences = new()
    {
        [typeof(PolicyEngine).Assembly] = [],
        [typeof(Notifier).Assembly] = ["NenFocus.Domain"],
        [typeof(SystemClock).Assembly] = ["NenFocus.Application", "NenFocus.Domain"],
    };

    [Fact]
    public void InnerLayersReferenceOnlyLayersFurtherIn()
    {
        var violations = AllowedReferences.SelectMany(layer =>
            layer.Key.GetReferencedAssemblies()
                .Select(r => r.Name!)
                .Where(name => name.StartsWith("NenFocus", StringComparison.Ordinal) && !layer.Value.Contains(name))
                .Select(name => $"{layer.Key.GetName().Name} → {name}"));

        KnownExceptions.Check(violations, []);
    }

    // The UI project references Infrastructure only so the composition root can wire it up.
    // Matches both "using NenFocus.Infrastructure…" and a partly qualified "Infrastructure.X".
    private static readonly Regex InfrastructureUse = new(@"\bInfrastructure\s*\.");

    private static readonly string[] CompositionRoot = ["NenFocus/App.axaml.cs", "NenFocus/Program.cs"];

    [Fact]
    public void OnlyTheCompositionRootUsesInfrastructureFromTheUi()
    {
        var violations = SourceCode.Files("NenFocus")
            .Where(f => !CompositionRoot.Contains(f.Path) && InfrastructureUse.IsMatch(f.Code))
            .Select(f => f.Path);

        KnownExceptions.Check(violations, []);
    }
}
