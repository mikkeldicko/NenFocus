namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// What may be done to a block at all, as ready-made answers for a screen (Gap 93): each is false
/// only when no change of that kind is possible for this block. Whether one concrete change is
/// allowed — a move to this slot, a shrink to that length — is asked for that change
/// (<see cref="Services.BlockChangeChecker"/>). Moving and resizing share one answer: no rule
/// tells them apart since a started block became fully locked (Gap 88).
/// </summary>
public sealed record BlockChangeAllowances(bool CanReschedule, bool CanChangePreset, bool CanSplit, bool CanDelete)
{
    /// <summary>
    /// Whether a split's pieces may land on another day than the block's own — so a screen only
    /// asks for a day per piece where one could be accepted. Never true unless <see cref="CanSplit"/>.
    /// </summary>
    public bool SplitPiecesMayChangeDay { get; init; }
}
