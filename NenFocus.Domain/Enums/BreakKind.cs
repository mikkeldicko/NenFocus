namespace NenFocus.Domain.Enums;

/// <summary>
/// The kind of a break in a session's BreakSchedule. The two are told apart so the UI and
/// notifications can treat them differently (the long-break marker on the Main page progress
/// bar, "Long break time"). Stored as text in SessionBreaks, so the value names stay.
/// </summary>
public enum BreakKind
{
    ShortBreak,
    LongBreak
}
