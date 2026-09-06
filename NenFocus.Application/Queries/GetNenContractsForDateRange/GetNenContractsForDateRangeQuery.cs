using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetNenContractsForDateRange;

/// <summary>
/// Every in-effect (Scheduled or Active) contract whose date range overlaps
/// [RangeStart, RangeEnd] (both inclusive), one entry per contract. RangeStart/RangeEnd
/// describe whatever span the caller currently has visible — a week today, potentially a
/// single day or a month once other calendar views exist — this query has no opinion on
/// span length. Results are ordered with the "default" contract first: an Active one is
/// preferred over a merely-Scheduled one, then earliest StartDate — the same tiebreak the
/// Planner has always used for its single-contract banner, now exposed as an ordering
/// instead of a filter, so callers with only one contract in range see no behavior change.
/// A contract's Day-X/Y and deficit figures are still computed relative to "today" (the
/// clock), independent of which range is viewed.
/// </summary>
public sealed record GetNenContractsForDateRangeQuery(DateOnly RangeStart, DateOnly RangeEnd)
    : IQuery<IReadOnlyList<NenContractRangeViewDto>>;
