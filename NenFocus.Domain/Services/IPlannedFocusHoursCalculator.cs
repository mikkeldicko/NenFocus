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
    /// A range that crosses midnight has its duration split proportionally between
    /// every calendar day it touches — e.g. 23:00-01:00 credits 1 hour to each day.
    /// </summary>
    IReadOnlyDictionary<DateOnly, double> Calculate(IEnumerable<(DateTime Start, DateTime End)> focusBlockRanges);
}
