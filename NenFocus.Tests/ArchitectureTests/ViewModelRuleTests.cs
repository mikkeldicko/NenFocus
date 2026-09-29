using System.Reflection;
using NenFocus.Application.Interfaces;
using NenFocus.ViewModels;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// The ViewModel Forbidden Zone (MODULE_STANDARDS → UI). Checked across the whole UI project
/// except the composition root, so code-behind can't go around it either.
/// </summary>
public class ViewModelRuleTests
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

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

    /// <summary>Every type a UI type holds or is handed: fields, properties, constructor and method
    /// parameters, return types — generic arguments included, so <c>Func&lt;IRepo&gt;</c> counts too.</summary>
    private static IEnumerable<Type> TypesUsedBy(Type type)
    {
        var used = type.GetFields(AllDeclared).Select(f => f.FieldType)
            .Concat(type.GetProperties(AllDeclared).Select(p => p.PropertyType))
            .Concat(type.GetConstructors(AllDeclared).SelectMany(c => c.GetParameters()).Select(p => p.ParameterType))
            .Concat(type.GetMethods(AllDeclared).Where(m => !m.Name.StartsWith('!')).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)));
        return used.SelectMany(Flatten).Distinct();
    }

    private static IEnumerable<Type> Flatten(Type type) =>
        type.IsGenericType
            ? type.GetGenericArguments().SelectMany(Flatten).Prepend(type)
            : type.HasElementType ? Flatten(type.GetElementType()!).Prepend(type) : [type];

    [Fact]
    public void NoViewModelTouchesARepository()
    {
        var violations = UiTypes()
            .SelectMany(t => TypesUsedBy(t)
                .Where(u => u.IsInterface && u.Name.EndsWith("Repository", StringComparison.Ordinal))
                .Select(u => $"{t.FullName} → {u.Name}"));

        KnownExceptions.Check(violations, []);
    }

    /// <summary>ViewModels take IQueryDispatcher/ICommandDispatcher, never a handler — a handler held by a
    /// root-resolved ViewModel shares one DbContext for the app's lifetime (Gap 40).</summary>
    [Fact]
    public void NoViewModelHoldsAHandler()
    {
        var violations = UiTypes()
            .SelectMany(t => TypesUsedBy(t)
                .Where(u => u.IsGenericType
                            && u.GetGenericTypeDefinition() is var d
                            && (d == typeof(IQueryHandler<,>) || d == typeof(ICommandHandler<,>)))
                .Select(u => $"{t.FullName} → {u.Name}"));

        KnownExceptions.Check(violations, []);
    }

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
