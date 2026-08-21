namespace NenFocus.Application.Queries.CheckRedemptionEntry;

/// <summary>
/// Maps a CheckRedemptionEntryQuery verdict to user-facing text — shared by
/// CreatePlannedBlockValidator and UpdatePlannedBlockValidator so the two can't drift
/// into different wording for the same failure reason.
/// </summary>
public static class RedemptionEntryMessages
{
    public static string? For(RedemptionEntryCheckResult result) => result switch
    {
        RedemptionEntryCheckResult.Valid => null,
        RedemptionEntryCheckResult.MissedBlockNotFound => "The referenced block doesn't exist or isn't linked to a Nen Contract.",
        RedemptionEntryCheckResult.PresetMismatch => "A redemption block can only cover missed blocks that use the same preset.",
        RedemptionEntryCheckResult.StrictnessDoesNotAllowRedemption => "This contract is Locked — missed hours can't be made up.",
        RedemptionEntryCheckResult.WrongDay => "Under this contract's strictness rule, a missed block can only be redeemed on the same day it was scheduled.",
        RedemptionEntryCheckResult.ExceedsRemainingDeficit => "This covers more minutes than the referenced block still has outstanding.",
        _ => throw new ArgumentOutOfRangeException(nameof(result))
    };
}
