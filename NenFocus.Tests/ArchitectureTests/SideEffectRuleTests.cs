using NenFocus.Application.Interfaces;
using NenFocus.Application.Services;
using NenFocus.Infrastructure.Services;
using NenFocus.ViewModels;

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
    /// never overwrite a fresh one. (App's shutdown path resolves it from the container — the
    /// composition root, not a holder.)</summary>
    [Fact]
    public void OnlyTheSynchronizerAppliesEnforcement()
    {
        var holders = AllTypes()
            .Where(t => TypeUse.TypesUsedBy(t).Contains(typeof(IEnforcementService)))
            .Select(t => t.FullName!);

        KnownExceptions.Check(holders.Where(h => h != typeof(EnforcementSynchronizer).FullName), []);
    }
}
