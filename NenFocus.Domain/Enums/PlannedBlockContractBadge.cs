namespace NenFocus.Domain.Enums;

/// <summary>
/// A block's special relationship to a contract, for display purposes only — never
/// persisted, always computed at read time. Redemption and Bonus are mutually exclusive
/// (Redemption always has ContractId set; Bonus never does), so a null badge already
/// covers both an ordinary committed block and one with no contract relation at all —
/// both fully distinguished by ContractId's own nullability, with no need to name them here.
/// </summary>
public enum PlannedBlockContractBadge
{
    Redemption,
    Bonus
}
