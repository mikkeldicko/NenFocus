using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>Where a break sits on a session's run — a fraction from 0 to 1 — and its kind.</summary>
public sealed record BreakMark(double ProgressFraction, BreakKind Kind);
