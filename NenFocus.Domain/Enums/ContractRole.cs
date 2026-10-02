namespace NenFocus.Domain.Enums;

/// <summary>
/// How a block belongs to its Nen Contract — set once when the block is made, never changed
/// (Gap 97). Committed: drafted and sealed. Redemption: placed to cover a deficit. Bonus: placed
/// on a contract in effect for any other reason. Read only inside the Domain, through
/// <see cref="ValueObjects.ContractMembership"/>'s named questions.
/// </summary>
public enum ContractRole
{
    Committed,
    Redemption,
    Bonus
}
