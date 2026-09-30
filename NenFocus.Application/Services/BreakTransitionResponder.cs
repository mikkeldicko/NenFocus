using NenFocus.Application.Interfaces;
using NenFocus.Application.Notifications;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Services;

/// <summary>
/// Replaces the former BreakStarted/BreakEnded event handlers: a break is no longer a
/// state change on the Session, only a boundary on its schedule the clock moved past
/// (Gap 63). PolicyEngine already reads the schedule, so SyncAsync alone decides what a
/// break lifts; this only makes that happen now rather than on the next heartbeat.
/// </summary>
public sealed class BreakTransitionResponder : IBreakTransitionResponder
{
    private readonly IEnforcementSynchronizer _synchronizer;
    private readonly INotifier _notifier;

    public BreakTransitionResponder(IEnforcementSynchronizer synchronizer, INotifier notifier)
    {
        _synchronizer = synchronizer;
        _notifier = notifier;
    }

    public async Task RespondAsync(
        IReadOnlyList<BreakTransition> transitions, Moment at, bool keepBlockingDuringBreaks, CancellationToken ct = default)
    {
        if (transitions.Count == 0) return;

        await _synchronizer.SyncAsync(at, ct);

        // Only the latest boundary describes where the session is now — after sleeping
        // through a whole break, "Break over" is true and "Break time" is stale.
        var latest = transitions[^1];
        NenNotification fact = latest.Kind == BreakTransitionKind.Started
            ? new BreakStarted(latest.BreakKind == SessionPhaseKind.LongBreak, keepBlockingDuringBreaks)
            : new BreakEnded();
        await _notifier.NotifyAsync(fact, ct);
    }
}
