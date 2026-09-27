using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Services;

/// <summary>
/// Replaces the former BreakStarted/BreakEnded event handlers: a break is no longer a
/// state change on the Session, only a boundary on its schedule the clock moved past
/// (Gap 63). PolicyEngine already reads the schedule, so SyncAsync alone decides what a
/// break lifts; this only makes that happen now rather than on the next heartbeat.
/// </summary>
public sealed class BreakTransitionHandler : IBreakTransitionHandler
{
    private readonly IEnforcementSynchronizer _synchronizer;
    private readonly INotificationService _notifications;

    public BreakTransitionHandler(IEnforcementSynchronizer synchronizer, INotificationService notifications)
    {
        _synchronizer = synchronizer;
        _notifications = notifications;
    }

    public async Task HandleAsync(
        IReadOnlyList<BreakTransition> transitions, DateTime at, bool keepBlockingDuringBreaks, CancellationToken ct = default)
    {
        if (transitions.Count == 0) return;

        await _synchronizer.SyncAsync(at, ct);

        // Only the latest boundary describes where the session is now — after sleeping
        // through a whole break, "Break over" is true and "Break time" is stale.
        var latest = transitions[^1];
        if (latest.Kind == BreakTransitionKind.Started)
        {
            var label = latest.BreakKind == SessionPhaseKind.LongBreak ? "Long break time" : "Break time";
            var body = keepBlockingDuringBreaks
                ? "Step away and recharge. Blocking stays on (contract rule)."
                : "Step away and recharge.";
            await _notifications.NotifyAsync(label, body, ct);
        }
        else
        {
            await _notifications.NotifyAsync("Break over", "Back to focus.", ct);
        }
    }
}
