using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

/// <summary>
/// Sums Focus-block time ranges into planned hours per calendar day. Stateless and
/// synchronous — the caller supplies exactly the ranges to count, whether they come
/// from persisted blocks, in-memory drafts (a Nen Contract wizard not yet sealed), or
/// a mix of both. Only Focus-block ranges should ever be passed in; filtering by block
/// type is the caller's concern, not this calculator's.
/// </summary>
public interface IPlannedFocusHoursCalculator
{
    /// <summary>
    /// Hours per the user's local calendar day, in real time (UserTimeZone.SplitByDay) — so a
    /// range crossing midnight credits each day its share (23:00-01:00: 1 hour each), and a DST night counts real hours.
    /// </summary>
    IReadOnlyDictionary<DateOnly, double> Calculate(IEnumerable<TimeSlot> focusTime, UserTimeZone timeZone);
}
