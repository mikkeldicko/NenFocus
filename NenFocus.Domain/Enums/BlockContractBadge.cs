namespace NenFocus.Domain.Enums;

/// <summary>
/// A block's special relationship to a contract, for display purposes only — read off the
/// block's stored role in its contract (Gap 97). A block has one role, so only one badge
/// can apply; a null badge is a block with no contract (or a Background block).
/// </summary>
public enum BlockContractBadge
{
    Committed,
    Redemption,
    Bonus
}
