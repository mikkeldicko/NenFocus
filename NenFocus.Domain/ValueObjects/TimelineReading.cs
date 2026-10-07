namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// A session at one moment (<see cref="SessionTimeline.ReadingAt"/>): time left, how far through,
/// the break it is in (if any) and how far through that, breaks begun, and whether its own preset's
/// blocking is lifted right now. Handed to the UI as it is (Gap 99).
/// </summary>
public sealed record TimelineReading(
    TimeSpan TimeRemaining,
    double Progress,
    SessionBreak? CurrentBreak,
    TimeSpan BreakTimeRemaining,
    double BreakProgress,
    int BreaksTaken,
    bool LiftsBlocking,
    bool HasEnded)
{
    /// <summary>Nothing running: no time left, no break.</summary>
    public static TimelineReading None { get; } = new(TimeSpan.Zero, 0, null, TimeSpan.Zero, 0, 0, false, false);

    public bool IsOnBreak => CurrentBreak is not null;

    /// <summary>On a break whose contract keeps the preset blocked through it — so the Main page can say so.</summary>
    public bool KeepsBlockingDuringBreak => IsOnBreak && !LiftsBlocking;
}
