namespace NenFocus.Domain.Enums;

/// <summary>
/// How a block belongs to its Nen Contract — set once when the block is made, never changed
/// (Gap 97). Committed: drafted and sealed. Redemption: placed to cover a deficit. Bonus: placed
/// on a contract in effect for any other reason. Rules read it only through
/// <see cref="ValueObjects.ContractMembership"/>'s named questions; outside the Domain it is shown, never decided on.
/// </summary>
public enum ContractRole
{
    Committed,
    Redemption,
    Bonus
}
