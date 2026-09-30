using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.GetFocusBlockRitualContext;

/// <summary>The planned block's window, as the Ritual needs it (Gap 88: a TimeSlot, not two DateTimes).</summary>
public sealed record FocusBlockSchedule
{
    public required TimeSlot Slot { get; init; }

    public int TotalMinutes => (int)Slot.Length.TotalMinutes;

    /// <summary>
    /// Minutes left before the block ends, as of the given moment — clamped to a 1-minute floor so a
    /// session about to start (or started a hair late) is never offered a 0-or-negative duration.
    /// Lives here, not in HatsuPageViewModel, so the ViewModel only ever asks the question
    /// (ViewModel Forbidden Zone — no time arithmetic in a ViewModel). Time to the end is the
    /// slot's length plus the (negative once started) time until its start.
    /// </summary>
    public int RemainingMinutes(Moment asOf) => Math.Max(1, (int)(Slot.Length + Slot.StartsIn(asOf)).TotalMinutes);

    public bool HasEnded(Moment asOf) => Slot.HasEnded(asOf);
}
