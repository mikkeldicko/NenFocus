using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// <see cref="Entities.NenContract.CheckRedemptionPlacement"/>'s answer (Gap 106): placed, with the
/// receipt the block would hold and its slot capped to what it covers — or refused, and why.
/// Values only, never an entity: the handler builds the block through
/// <see cref="Entities.NenContract.PlaceRedemptionBlock"/>.
/// </summary>
public sealed record RedemptionVerdict
{
    public RedemptionPlacementOutcome Outcome { get; }

    /// <summary>What the block would cover; <see cref="RedemptionReceipt.None"/> unless placed.</summary>
    public RedemptionReceipt Receipt { get; }

    /// <summary>The slot that would be saved, capped to the receipt; null unless placed.</summary>
    public TimeSlot? Slot { get; }

    /// <summary>
    /// The slot was shortened to what it covers — always by whole minutes, so any shortening is worth
    /// telling the user (Gap 92, V10: suggestions are now exactly sized, no sub-minute margin to ignore).
    /// </summary>
    public bool WasCapped { get; }

    /// <summary>
    /// The contract's Redemption membership the block would hold; null unless placed. Carried so a
    /// validator can check placement as the block will be saved (Design Review Rule 16) — the
    /// Application can't make a Redemption membership itself.
    /// </summary>
    public ContractMembership? Membership { get; }

    private RedemptionVerdict(
        RedemptionPlacementOutcome outcome, RedemptionReceipt receipt, TimeSlot? slot, bool wasCapped, ContractMembership? membership)
    {
        Outcome = outcome;
        Receipt = receipt;
        Slot = slot;
        WasCapped = wasCapped;
        Membership = membership;
    }

    internal static RedemptionVerdict Refused(RedemptionPlacementOutcome outcome) =>
        new(outcome, RedemptionReceipt.None, null, false, null);

    internal static RedemptionVerdict Placed(RedemptionReceipt receipt, TimeSlot slot, bool wasCapped, ContractMembership membership) =>
        new(RedemptionPlacementOutcome.Placed, receipt, slot, wasCapped, membership);
}
