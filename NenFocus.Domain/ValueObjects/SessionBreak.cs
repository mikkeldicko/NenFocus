using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// One break in a session's BreakSchedule: its kind plus its [Start, End) offset from
/// the session's own StartedAt. Offsets, not clock times, so a schedule exists before
/// the session starts and means the same thing whenever it does (Gap 63).
/// </summary>
public sealed record SessionBreak
{
    public SessionPhaseKind Kind { get; }
    public TimeSpan Start { get; }
    public TimeSpan End { get; }

    public SessionBreak(SessionPhaseKind kind, TimeSpan start, TimeSpan end)
    {
        if (kind == SessionPhaseKind.Work)
            throw new ArgumentException("A break must be a ShortBreak or LongBreak.", nameof(kind));
        if (start <= TimeSpan.Zero)
            throw new ArgumentException("A break must start after the session does.", nameof(start));
        if (end <= start)
            throw new ArgumentException("A break must end after it starts.", nameof(end));

        Kind = kind;
        Start = start;
        End = end;
    }

    public TimeSpan Length => End - Start;

    /// <summary>Time left in the break at <paramref name="offset"/> (from the session's start).</summary>
    public TimeSpan RemainingAt(TimeSpan offset) => End - offset;

    /// <summary>How far through the break <paramref name="offset"/> is, from 0 to 1.</summary>
    public double ProgressAt(TimeSpan offset) => Math.Clamp((offset - Start) / Length, 0.0, 1.0);
}
