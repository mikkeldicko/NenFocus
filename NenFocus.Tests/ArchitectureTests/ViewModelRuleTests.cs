using NenFocus.Application.Interfaces;
using NenFocus.ViewModels;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// The ViewModel Forbidden Zone (MODULE_STANDARDS → UI). Checked across the whole UI project
/// except the composition root, so code-behind can't go around it either.
/// </summary>
public class ViewModelRuleTests
{
    private static readonly string[] CompositionRoot = ["NenFocus.App", "NenFocus.Program"];
    private static readonly string[] CompositionRootFiles = ["NenFocus/App.axaml.cs", "NenFocus/Program.cs"];

    // Avalonia's compiled XAML is generated code, not ours, and takes IServiceProvider by design:
    // its own namespace, nested XamlClosure_<n> types, and members named with '!' (impossible in C#).
    private static IEnumerable<Type> UiTypes() =>
        typeof(ShellViewModel).Assembly.GetTypes()
            .Where(t => !CompositionRoot.Any(root => t.FullName == root || t.FullName!.StartsWith(root + "+", StringComparison.Ordinal)))
            .Where(t => !t.FullName!.StartsWith("CompiledAvaloniaXaml", StringComparison.Ordinal)
                        && !GeneratedXamlClosure.IsMatch(t.FullName));

    private static readonly System.Text.RegularExpressions.Regex GeneratedXamlClosure = new(@"\+XamlClosure_\d+(\+|$)");

    private static IEnumerable<Type> TypesUsedBy(Type type) => TypeUse.TypesUsedBy(type);

    [Fact]
    public void NoViewModelTouchesARepository()
    {
        var violations = UiTypes()
            .SelectMany(t => TypesUsedBy(t)
                .Where(u => u.IsInterface && u.Name.EndsWith("Repository", StringComparison.Ordinal))
                .Select(u => $"{t.FullName} → {u.Name}"));

        KnownExceptions.Check(violations, []);
    }

    // "No handlers" lives in DispatcherRuleTests — one rule for the UI and Infrastructure alike.

    private static readonly System.Text.RegularExpressions.Regex DomainEntityOrServiceUse = new(@"\bDomain\s*\.\s*(Entities|Services)\b");

    private static readonly string[] DomainUseExceptions =
    [
        // Reads FocusBlock.MaxDurationHours to cap the End dropdown — a business rule in the UI. → Gap 89 area 2.
        "NenFocus/ViewModels/Components/PlannerEndTimeOptions.cs",
    ];

    /// <summary>The UI sees the Domain only through Application's DTOs — never an entity, never a
    /// Domain service. A source scan, not reflection: an entity's constant is copied into the caller at
    /// compile time, so the compiled UI wouldn't mention the entity at all.</summary>
    [Fact]
    public void NoUiCodeUsesDomainEntitiesOrServices()
    {
        var violations = SourceCode.Files("NenFocus")
            .Where(f => DomainEntityOrServiceUse.IsMatch(f.Code))
            .Select(f => f.Path);

        KnownExceptions.Check(violations, DomainUseExceptions);
    }

    // Any timer type, or a Task.Delay loop — in Views too, so polling can't just move there.
    // Visual effects use Avalonia's animations/transitions, not code timers.
    private static readonly System.Text.RegularExpressions.Regex TimerUse =
        new(@"\b(Timers\s*\.\s*Timer|Threading\s*\.\s*Timer|DispatcherTimer|PeriodicTimer)\b|\bnew\s+Timer\s*\(|\.\s*Delay\s*\(");

    private static readonly string[] TimerExceptions =
    [
        // 3 s poll so today's badge counts up during a session. → Gap 89 area 3 (the heartbeat announces what changed).
        "NenFocus/ViewModels/Pages/PlannerPageViewModel.cs",
    ];

    /// <summary>No UI code owns a timer; it subscribes to a Tick from a service that does
    /// (MODULE_STANDARDS → No Timers; canonical: IActiveSessionMonitor.Tick → MainPageViewModel).</summary>
    [Fact]
    public void NoUiCodeOwnsATimer()
    {
        var violations = SourceCode.Files("NenFocus")
            .Where(f => !CompositionRootFiles.Contains(f.Path) && TimerUse.IsMatch(f.Code))
            .Select(f => f.Path);

        KnownExceptions.Check(violations, TimerExceptions);
    }

    // Formatting a value: a format specifier in an interpolation ({x:HH:mm}), ToString("…"),
    // string.Format, or a converter's static Format… helper. Plain copy built from names is allowed
    // (MODULE_STANDARDS → No String Formatting → What counts).
    private static readonly System.Text.RegularExpressions.Regex ValueFormatting = new(
        @"\.ToString\(\s*@?""|\{[^{}""]+:[A-Za-z0-9#.,:]+\}|\b[Ss]tring\s*\.\s*Format\s*\(|\b\w+Converter\s*\.\s*Format\w*\s*\(");

    private static readonly string[] ValueFormattingExceptions =
    [
        // All → Gap 89 (ViewModel audit): values should reach the View raw and be formatted by a converter.
        "NenFocus/ViewModels/Pages/PlannerPageViewModel.cs has 35",
        "NenFocus/ViewModels/Pages/ContractPresetPageViewModel.cs has 6",
        "NenFocus/ViewModels/Components/DayFloorPreviewViewModel.cs has 6",
        "NenFocus/ViewModels/Contracts/ContractWizardViewModel.cs has 3",
        "NenFocus/ViewModels/Pages/PostContractViewModel.cs has 2",
        "NenFocus/ViewModels/Contracts/ContractRulesStepViewModel.cs has 2",
        "NenFocus/ViewModels/Components/HourLabelViewModel.cs has 1",
    ];

    /// <summary>A ViewModel exposes the raw value; a converter turns it into text (MODULE_STANDARDS → No String
    /// Formatting). Entries are "File has N", so a new format call in a listed file fails too.</summary>
    [Fact]
    public void NoViewModelFormatsValues()
    {
        var violations = SourceCode.Files("NenFocus/ViewModels")
            .Select(f => (f.Path, Count: ValueFormatting.Matches(f.Code).Count))
            .Where(x => x.Count > 0)
            .Select(x => $"{x.Path} has {x.Count}");

        KnownExceptions.Check(violations, ValueFormattingExceptions);
    }

    private static readonly System.Text.RegularExpressions.Regex MathCall = new(@"\bMath\s*\.\s*\w+\s*\(");

    private static readonly string[] MathExceptions =
    [
        // Allowed: Planner pixel geometry — drag/resize positions, tile layout, the dropdown's visible window
        // (MODULE_STANDARDS → UI → Views: pixel geometry yes, business rules never). Helpers in ViewModels/Components.
        "NenFocus/ViewModels/Components/PlannerDragMath.cs",
        "NenFocus/ViewModels/Components/PlannerLayout.cs",
        "NenFocus/ViewModels/Components/PlannerDropdownWindow.cs",
    ];

    /// <summary>A ViewModel binds ready-made values — progress, remaining time — computed by a service
    /// (MODULE_STANDARDS → No Math). Catches Math.*; plain + − × ÷ is beyond a text scan.</summary>
    [Fact]
    public void NoViewModelDoesMath()
    {
        var violations = SourceCode.Files("NenFocus/ViewModels")
            .Where(f => MathCall.IsMatch(f.Code))
            .Select(f => f.Path);

        KnownExceptions.Check(violations, MathExceptions);
    }

    /// <summary>The dispatchers are the UI's one door to data. A service locator would be a second,
    /// invisible one: it goes around both rules above without showing in any constructor.</summary>
    [Fact]
    public void NoViewModelHoldsAServiceLocator()
    {
        var violations = UiTypes()
            .SelectMany(t => TypesUsedBy(t)
                .Where(u => u == typeof(IServiceProvider) || u == typeof(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory))
                .Select(u => $"{t.FullName} → {u.Name}"));

        KnownExceptions.Check(violations, []);
    }
}
