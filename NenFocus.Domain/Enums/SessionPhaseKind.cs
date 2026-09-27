namespace NenFocus.Domain.Enums;

/// <summary>
/// The kind of a stretch of a session. A SessionBreak is ShortBreak or LongBreak;
/// Work is everything between breaks (never stored — see BreakSchedule). The two
/// break kinds are distinguished so the UI and enforcement can treat them
/// differently (e.g. the long-break marker rendering distinctly on the Main
/// Page progress bar).
/// </summary>
public enum SessionPhaseKind
{
    Work,
    ShortBreak,
    LongBreak
}
