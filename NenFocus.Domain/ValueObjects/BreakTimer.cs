namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// The frozen work/break rhythm a Session runs under, copied by value from a
/// BreakTimerPreset at Start — mirrors ContractPreset's "copied by value, not a
/// live reference" rule exactly. Editing or deleting the source preset later has
/// no effect on a session that already captured its own copy. Replaces
/// BreakSettings, which only ever modeled a single fixed interval/length pair
/// with no concept of cycles or a long break.
/// </summary>
public record BreakTimer
{
    public int WorkMinutes { get; }
    public int ShortBreakMinutes { get; }
    public int CyclesBeforeLongBreak { get; }
    public int LongBreakMinutes { get; }

    public BreakTimer(int workMinutes, int shortBreakMinutes, int cyclesBeforeLongBreak, int longBreakMinutes)
    {
        if (workMinutes <= 0)
            throw new ArgumentException("Work minutes must be positive.", nameof(workMinutes));
        if (shortBreakMinutes <= 0)
            throw new ArgumentException("Short break minutes must be positive.", nameof(shortBreakMinutes));
        if (cyclesBeforeLongBreak <= 0)
            throw new ArgumentException("Cycles before long break must be positive.", nameof(cyclesBeforeLongBreak));
        if (longBreakMinutes <= 0)
            throw new ArgumentException("Long break minutes must be positive.", nameof(longBreakMinutes));

        WorkMinutes = workMinutes;
        ShortBreakMinutes = shortBreakMinutes;
        CyclesBeforeLongBreak = cyclesBeforeLongBreak;
        LongBreakMinutes = longBreakMinutes;
    }
}
