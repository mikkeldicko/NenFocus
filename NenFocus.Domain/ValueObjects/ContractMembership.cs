using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// A block's membership in a Nen Contract: which contract, and how it belongs. The two always
/// come together — a block has one membership or none (Gap 97). Only the Domain makes one (the
/// contract, or the drafting batch). The role may be read — the Planner shows it as a tag — but
/// every rule asks the questions below, never the role itself.
/// </summary>
public sealed record ContractMembership
{
    internal ContractMembership(Guid contractId, ContractRole role)
    {
        if (contractId == Guid.Empty)
            throw new ArgumentException("A membership needs a contract.", nameof(contractId));
        ContractId = contractId;
        Role = role;
    }

    // For EF only: it maps ContractId/Role by name onto the block's own columns (NenFocusDbContext).
    private ContractMembership() { }

    public Guid ContractId { get; }

    public ContractRole Role { get; }

    /// <summary>Committed and Redemption blocks count toward the contract's floor; Bonus never does.</summary>
    public bool CountsTowardFloor => Role is ContractRole.Committed or ContractRole.Redemption;

    public bool IsCommitted => Role == ContractRole.Committed;

    public bool IsRedemption => Role == ContractRole.Redemption;

    public bool IsBonus => Role == ContractRole.Bonus;
}
