namespace NenFocus.Application.DTOs;

/// <summary>Application-layer mirror of the Domain BreakTimer value object — used
/// wherever a session's frozen cadence needs to cross into the UI (SessionDto),
/// replacing BreakSettingsDto.</summary>
public sealed record BreakTimerDto
{
    public int WorkMinutes { get; init; }
    public int ShortBreakMinutes { get; init; }
    public int CyclesBeforeLongBreak { get; init; }
    public int LongBreakMinutes { get; init; }
}
