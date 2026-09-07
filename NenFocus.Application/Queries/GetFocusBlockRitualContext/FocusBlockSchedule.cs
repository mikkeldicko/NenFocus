namespace NenFocus.Application.Queries.GetFocusBlockRitualContext;

public sealed record FocusBlockSchedule
{
    public DateTime Start { get; init; }
    public DateTime End { get; init; }

    public int TotalMinutes => (int)(End - Start).TotalMinutes;

    /// <summary>
    /// Minutes left before End, as of the given instant — clamped to a 1-minute floor so a
    /// session about to start (or started a hair late) is never offered a 0-or-negative
    /// duration. Lives here, not in HatsuPageViewModel, so the ViewModel only ever asks the
    /// question rather than deriving it from raw Start/End itself (ViewModel Forbidden Zone —
    /// no TimeSpan arithmetic in a ViewModel).
    /// </summary>
    public int RemainingMinutes(DateTime asOf) => Math.Max(1, (int)(End - asOf).TotalMinutes);
}
