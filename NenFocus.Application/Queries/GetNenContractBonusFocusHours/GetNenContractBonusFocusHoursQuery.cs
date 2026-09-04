using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetNenContractBonusFocusHours;

/// <summary>
/// Completed Focus hours per calendar day from Bonus blocks only — FocusBlocks with no
/// ContractId, whose Start falls within this contract's own [StartDate, EndDate] range.
/// Mirrors GetNenContractCompletedFocusHoursQuery exactly, inverted: that query sums every
/// block WITH this ContractId (committed or redemption); this one sums every block WITHOUT
/// one that still belongs to this contract's window. Same Bonus-resolution rule
/// GetPlannedBlocksForDayQueryHandler already uses for the tile-level badge — a block's
/// Start (a single instant) can fall inside at most one contract's range, since contract
/// ranges never overlap, so "is this Bonus for this contract" is answered identically
/// everywhere it's asked. Feeds the Planner's per-day Bonus indicator and the active-
/// contract banner's running Bonus total — display only, never counted toward any floor.
/// </summary>
public sealed record GetNenContractBonusFocusHoursQuery(Guid ContractId) : IQuery<IReadOnlyDictionary<DateOnly, double>>;
