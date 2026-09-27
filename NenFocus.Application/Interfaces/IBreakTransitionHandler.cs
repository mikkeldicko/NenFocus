using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Interfaces;

/// <summary>
/// What happens when a running session crosses break boundaries (Gap 63): blocking is
/// brought in line at once, and the user is told. Called by the clock-watching monitor
/// with every boundary the session reported crossing since its last check — usually one,
/// more if the clock jumped (sleep, a restart). keepBlockingDuringBreaks is the session's own
/// contract rule (Gap 64), so the user is told when a break won't lift blocking.
/// </summary>
public interface IBreakTransitionHandler
{
    Task HandleAsync(IReadOnlyList<BreakTransition> transitions, DateTime at, bool keepBlockingDuringBreaks, CancellationToken ct = default);
}
