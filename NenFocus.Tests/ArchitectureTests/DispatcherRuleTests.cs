using System.Text.RegularExpressions;
using NenFocus.Application.Interfaces;
using NenFocus.Infrastructure.Services;
using NenFocus.ViewModels;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// Outside Application, a use case runs only through IQueryDispatcher/ICommandDispatcher — the one
/// door that creates a fresh scope per call and hides "resolve the handler, call it". A long-lived
/// ViewModel or service holding a handler shares one DbContext for the app's lifetime (Gap 40); one
/// fetching handlers by hand repeats what the dispatcher already hides. Application code depends on
/// handlers directly — MODULE_STANDARDS allows that.
/// </summary>
public class DispatcherRuleTests
{
    private static readonly string[] CompositionRoot = ["NenFocus.App", "NenFocus.Program"];

    private static bool IsHandler(Type t) =>
        t.IsGenericType && t.GetGenericTypeDefinition() is var d
        && (d == typeof(IQueryHandler<,>) || d == typeof(ICommandHandler<,>));

    [Fact]
    public void OutsideApplicationNothingHoldsAHandler()
    {
        var violations = new[] { typeof(SystemClock), typeof(ShellViewModel) }
            .SelectMany(a => a.Assembly.GetTypes())
            .Where(t => !CompositionRoot.Any(root => t.FullName == root || t.FullName!.StartsWith(root + "+", StringComparison.Ordinal)))
            // Generated code (Avalonia's compiled XAML, the compiler's async/lambda classes) isn't ours to judge here.
            .Where(t => !t.FullName!.StartsWith("CompiledAvaloniaXaml", StringComparison.Ordinal) && !t.FullName.Contains('<'))
            // A handler may wrap a handler: the transaction and validation wrappers.
            .Where(t => !t.GetInterfaces().Any(IsHandler))
            .Where(t => t != typeof(ScopedCommandDispatcher) && t != typeof(ScopedQueryDispatcher))
            .SelectMany(t => TypeUse.TypesUsedBy(t).Where(IsHandler).Select(u => $"{t.FullName} → {u.Name}"));

        KnownExceptions.Check(violations, []);
    }

    private static readonly Regex FetchesAHandler = new(
        @"\b(GetRequiredService|GetService)\s*<\s*I(Command|Query)Handler\s*<|\btypeof\s*\(\s*I(Command|Query)Handler\s*<");

    // Where fetching a handler is the job: the two dispatchers, the DI setup that wraps every handler
    // in validation and a transaction, and the UI's composition root.
    private static readonly string[] Homes =
    [
        "NenFocus.Infrastructure/Services/ScopedCommandDispatcher.cs",
        "NenFocus.Infrastructure/Services/ScopedQueryDispatcher.cs",
        "NenFocus.Infrastructure/DependencyInjection.cs",
        "NenFocus/App.axaml.cs",
        "NenFocus/Program.cs",
    ];

    private static readonly string[] FetchExceptions =
    [
        // → Gap 89 area 3: rebuilt into one heartbeat that dispatches (one scope per call, reads fresh after a save).
        "NenFocus.Infrastructure/Services/ScheduledEnforcementService.cs",
        "NenFocus.Infrastructure/Services/NenContractHeartbeatService.cs",
        "NenFocus.Infrastructure/Services/ActiveSessionMonitor.cs",
        // → Gap 89 area 4: startup rehydration (with the SessionDto copies, F1).
        "NenFocus.Infrastructure/Services/SessionStateService.cs",
    ];

    [Fact]
    public void OutsideApplicationNothingFetchesAHandler()
    {
        var violations = SourceCode.Files("NenFocus.Infrastructure", "NenFocus")
            .Where(f => !Homes.Contains(f.Path) && FetchesAHandler.IsMatch(f.Code))
            .Select(f => f.Path);

        KnownExceptions.Check(violations, FetchExceptions);
    }
}
