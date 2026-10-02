using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.DTOs;

/// <summary>
/// The answer to "may this block be changed or deleted?" as validators and the UI read it — the
/// outcome, plus the one user-facing sentence (Gap 93). The sibling of BlockPlacementVerdictDto.
/// </summary>
public sealed record BlockChangeVerdictDto
{
    public ChangeOutcome Outcome { get; init; }
    public string? Message { get; init; }

    /// <summary>
    /// The one builder, and the one home of the change-permission wording: the Update, Split and
    /// Delete validators all call it. Gap 100 moves the sentences out to the front end.
    /// </summary>
    public static BlockChangeVerdictDto From(ChangeVerdict verdict) => new()
    {
        Outcome = verdict.Outcome,
        Message = verdict.Outcome switch
        {
            ChangeOutcome.Allowed => null,
            ChangeOutcome.BlockHasStarted => "This block has already started — it cannot be changed.",
            ChangeOutcome.BlockIsRunning => "This block is running and cannot be deleted until it ends.",
            ChangeOutcome.ContractIsOver => "This block belongs to a finished Nen Contract and can no longer be changed.",
            ChangeOutcome.ContractBlockNeverDeleted => "Cannot delete a block that belongs to a Nen Contract.",
            ChangeOutcome.NoMovementAllowed => "This block is locked by a Lockdown contract.",
            ChangeOutcome.OutsideOwnDay => "Daily Flex contracts only allow moving blocks within the same day.",
            ChangeOutcome.OutsideContractPeriod =>
                "A contract's block cannot be moved outside the contract's period. " +
                "A Bonus block that isn't bound can be deleted and created again there.",
            ChangeOutcome.PresetFixed => "This block's preset cannot be changed while it belongs to a sealed contract.",
            ChangeOutcome.SplitNotOffered => "Only a Focus block that a Nen Contract binds can be split.",
            ChangeOutcome.BelowDailyFloor =>
                $"This would drop {verdict.Day:MMM d}'s total below its {verdict.FloorHours:0.#}h daily floor.",
            ChangeOutcome.BelowPeriodTarget =>
                $"This would drop the contract's total planned hours below its {verdict.FloorHours:0.#}h target.",
            _ => null
        }
    };
}
