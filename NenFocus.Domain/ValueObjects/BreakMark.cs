namespace NenFocus.Domain.ValueObjects;

/// <summary>Where a break sits on a session's run — a fraction from 0 to 1 — and whether it is the long one.</summary>
public sealed record BreakMark(double ProgressFraction, bool IsLongBreak);
