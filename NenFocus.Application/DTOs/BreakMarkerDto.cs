namespace NenFocus.Application.DTOs;

/// <summary>
/// One break's position on the Main Page's session progress bar — a ready-to-bind
/// fraction, not a raw domain type, so the ViewModel places a tick mark without
/// doing any math of its own (ViewModel Forbidden Zone — No Math). Computed once
/// from IBreakScheduleCalculator's phase timeline, the same timeline that drives
/// the session's own automatic work/break transitions.
/// </summary>
public sealed record BreakMarkerDto
{
    public double ProgressFraction { get; init; }
    public bool IsLongBreak { get; init; }
}
