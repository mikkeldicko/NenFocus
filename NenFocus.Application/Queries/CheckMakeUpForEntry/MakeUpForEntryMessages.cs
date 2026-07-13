namespace NenFocus.Application.Queries.CheckMakeUpForEntry;

/// <summary>
/// Maps a CheckMakeUpForEntryQuery verdict to user-facing text — shared by
/// CreatePlannedBlockValidator and UpdatePlannedBlockValidator so the two can't drift
/// into different wording for the same failure reason.
/// </summary>
public static class MakeUpForEntryMessages
{
    public static string? For(MakeUpForEntryCheckResult result) => result switch
    {
        MakeUpForEntryCheckResult.Valid => null,
        MakeUpForEntryCheckResult.MissedBlockNotFound => "The referenced block doesn't exist or isn't linked to a Nen Contract.",
        MakeUpForEntryCheckResult.PresetMismatch => "A make-up block can only cover missed blocks that use the same preset.",
        MakeUpForEntryCheckResult.StrictnessDoesNotAllowMakeUp => "This contract is Locked — missed hours can't be made up.",
        MakeUpForEntryCheckResult.WrongDay => "Under this contract's strictness rule, a missed block can only be made up on the same day it was scheduled.",
        MakeUpForEntryCheckResult.ExceedsRemainingDeficit => "This covers more minutes than the referenced block still has outstanding.",
        _ => throw new ArgumentOutOfRangeException(nameof(result))
    };
}
