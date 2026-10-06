using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetNenContractCompletedFocusHours;

/// <summary>
/// Completed Focus hours per calendar day for the contract's full date range — feeds
/// NenContract.IsDayMet(date, hours). Sums planned-block sessions (including redemption
/// blocks, which are ordinary FocusBlocks under the contract), applying the grace-period
/// credit via FocusBlock.CreditedSlot, plus Spontaneous sessions whose preset
/// matches any preset used by this contract. A block's credited minutes are attributed
/// entirely to its scheduled Start date — not split across midnight the way planned
/// (not-yet-completed) hours are by IPlannedFocusHoursCalculator; splitting completed
/// minutes across a grace-period-credited or late-started block is a materially harder,
/// separate question not covered by this slice.
/// </summary>
public sealed record GetNenContractCompletedFocusHoursQuery(Guid ContractId) : IQuery<IReadOnlyDictionary<DateOnly, double>>;
