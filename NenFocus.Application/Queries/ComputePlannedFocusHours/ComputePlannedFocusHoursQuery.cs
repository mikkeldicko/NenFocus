using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.ComputePlannedFocusHours;

/// <summary>
/// Planned Focus hours per calendar day for a schedule being drafted — the Contract Wizard's live
/// "hours scheduled" feedback before any contract exists. A sealed contract's days are
/// <c>NenContract.DayStatuses</c> (Gap 95).
/// </summary>
/// <param name="DraftFocusBlockRanges">The drafted, not-yet-saved Focus block ranges.</param>
public sealed record ComputePlannedFocusHoursQuery(
    IReadOnlyList<TimeSlot> DraftFocusBlockRanges) : IQuery<IReadOnlyDictionary<DateOnly, double>>;
