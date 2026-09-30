using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.DTOs;

/// <summary>
/// The answer to "may this block go here?" as the UI and validators read it — the outcome, plus
/// the one user-facing sentence, so the Planner's preview and a validator's save show the same
/// text. Gap 88 phase 8.
/// </summary>
public sealed record BlockPlacementVerdictDto
{
    public PlacementOutcome Outcome { get; init; }
    public string? Message { get; init; }

    /// <summary>
    /// The one builder, and the one home of the placement wording: the verdict query and Split's
    /// validator (which asks the checker directly, once per half) both call it. Gap 100 moves the
    /// sentences out to the front end.
    /// </summary>
    public static BlockPlacementVerdictDto From(PlacementVerdict verdict) => new()
    {
        Outcome = verdict.Outcome,
        Message = verdict.Outcome switch
        {
            PlacementOutcome.Allowed => null,
            PlacementOutcome.InThePast => "Start time must be in the future.",
            PlacementOutcome.OutsideContractHours => "This block falls outside the contract's configured Work Hours for that day.",
            PlacementOutcome.TimeTaken => "A Focus block already exists in this time window.",
            _ => null
        }
    };
}
