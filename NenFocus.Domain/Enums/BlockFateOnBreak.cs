namespace NenFocus.Domain.Enums;

/// <summary>
/// What happens to one of a Nen Contract's blocks when the contract is broken — the contract's
/// own answer (<c>NenContract.FateWhenBroken</c>, Gap 97). Cancel: it is cut short and kept as
/// history. Release: it stays on the calendar as a plain block. Leave: nothing changes.
/// </summary>
public enum BlockFateOnBreak
{
    Cancel,
    Release,
    Leave
}
