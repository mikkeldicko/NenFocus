namespace NenFocus.Domain.Enums;

/// <summary>
/// One segment of a BreakTimerPreset-driven session's phase timeline — see
/// IBreakScheduleCalculator. Work is a focus stretch; ShortBreak and LongBreak
/// are rest stretches, distinguished so the UI and enforcement can treat them
/// differently (e.g. the long-break marker rendering distinctly on the Main
/// Page progress bar).
/// </summary>
public enum SessionPhaseKind
{
    Work,
    ShortBreak,
    LongBreak
}
