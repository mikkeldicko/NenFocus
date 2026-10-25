using NenFocus.Application.Interfaces;
using NenFocus.Application.Services;
using NenFocus.Infrastructure.Services;
using NenFocus.ViewModels.Shell;

namespace NenFocus.Tests.ArchitectureTests;

/// <summary>
/// Side effects go through their one door (ARCHITECT_GOVERNANCE.md → Key patterns):
/// commands change state and raise domain events; event handlers turn those into enforcement
/// and notifications; only EnforcementSynchronizer turns a policy into an apply/remove (Gap 62).
/// </summary>
public class SideEffectRuleTests
{
    // The composition root (App, Program) wires things up and may resolve anything — including the
    // compiler's hidden classes for their async methods, which hold local variables as fields.
    private static readonly string[] CompositionRoot = ["NenFocus.App", "NenFocus.Program"];

    private static IEnumerable<Type> AllTypes() =>
        new[] { typeof(Notifier), typeof(SystemClock), typeof(ShellViewModel) }
            .SelectMany(t => t.Assembly.GetTypes())
            .Where(t => !CompositionRoot.Any(root => t.FullName == root || t.FullName!.StartsWith(root + "+", StringComparison.Ordinal)));

    private static readonly Type[] SideEffectDoors =
        [typeof(IEnforcementSynchronizer), typeof(IEnforcementService), typeof(INotifier), typeof(INotificationService)];

    /// <summary>A command never blocks, unblocks or notifies itself — its event handlers do, so every
    /// way into a state change gets the same side effects.</summary>
    [Fact]
    public void CommandHandlersLeaveSideEffectsToEventHandlers()
    {
        var violations = AllTypes()
            .Where(t => t.Name.EndsWith("CommandHandler", StringComparison.Ordinal))
            .SelectMany(t => TypeUse.TypesUsedBy(t)
                .Where(u => SideEffectDoors.Contains(u))
                .Select(u => $"{t.FullName} → {u.Name}"));

        KnownExceptions.Check(violations, []);
    }

    /// <summary>One locked step computes the slice and applies or removes it, so a stale answer can
    /// never overwrite a fresh one. No exemption — not even the composition root: since Gap 101 the
    /// shutdown path asks the synchronizer too. Includes the compiler's hidden classes, so a local
    /// variable inside an async method counts.</summary>
    [Fact]
    public void OnlyTheSynchronizerAppliesEnforcement()
    {
        var holders = new[] { typeof(Notifier), typeof(SystemClock), typeof(ShellViewModel) }
            .SelectMany(t => t.Assembly.GetTypes())
            .Where(t => TypeUse.TypesUsedBy(t).Contains(typeof(IEnforcementService)))
            .Select(t => t.FullName!);

        KnownExceptions.Check(holders.Where(h => h != typeof(EnforcementSynchronizer).FullName), []);
    }

    // A call to one of the relay's announcements, through any field or local.
    private static readonly System.Text.RegularExpressions.Regex RelaysAScreen =
        new(@"\.\s*Notify(PostSessionsReady|PostSessionAcknowledged|PostContractsReady)\s*\(");

    private static readonly string[] RelayExceptions =
    [
        // The missed-blocks sweep's batch: live triage needs every missed screen at once. → Gap 107.
        "NenFocus.Infrastructure/Services/HeartbeatService.cs",
        // "Acknowledged" when the screen closes, not after the save. → Gap 107.
        "NenFocus/ViewModels/Shell/ShellViewModel.cs",
    ];

    /// <summary>A screen or recap is relayed only by an event handler, after the commit, so it shows what
    /// was saved — whoever caused it (Gap 110; Gap 89 Slice A made the Post-Session screen one path).</summary>
    [Fact]
    public void OnlyEventHandlersRelayScreens()
    {
        var violations = SourceCode.Files("NenFocus.Application", "NenFocus.Infrastructure", "NenFocus")
            .Where(f => !f.Path.StartsWith("NenFocus.Application/EventHandlers/", StringComparison.Ordinal)
                        && RelaysAScreen.IsMatch(f.Code))
            .Select(f => f.Path);

        KnownExceptions.Check(violations, RelayExceptions);
    }
}
