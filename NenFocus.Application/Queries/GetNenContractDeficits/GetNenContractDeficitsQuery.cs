using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetNenContractDeficits;

/// <summary>
/// Returns every missed or late Focus Block under the contract that still carries
/// uncovered deficit — feeds the Planner's "Redemption" checklist. Only accounts for
/// deficit covered by linked redemption blocks (RedemptionEntries); spontaneous redemption
/// sessions don't yet write back to this ledger — see the Spontaneous redemption path
/// slice (not yet built) for that follow-up.
///
/// ExcludeCoverageFromBlockId excludes one specific block's own RedemptionEntries from the
/// "how much is already covered" sum. Needed when recomputing a redemption block's own
/// coverage on edit (UpdatePlannedBlockCommandHandler, via ComputeRedemptionAttributionQuery):
/// the block being edited still carries its stale, pre-edit entries in the DB until the
/// edit itself is saved, so without this exclusion the block would see its own prior
/// contribution as "already covered by someone else" and shrink its own new entry toward
/// zero on every edit. Null (the default) for every other caller — ordinary display of the
/// deficit list has no "block currently being recomputed" to exclude.
/// </summary>
public sealed record GetNenContractDeficitsQuery(Guid ContractId, Guid? ExcludeCoverageFromBlockId = null)
    : IQuery<IReadOnlyList<FocusBlockDeficitDto>>;
