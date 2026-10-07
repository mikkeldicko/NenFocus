using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

/// <summary>
/// The answer to "may a Cover Deficit tile go here?" as a validator reports it — the outcome,
/// plus the one user-facing sentence. The one home of the Cover Deficit wording (Gap 106);
/// precedent <see cref="BlockPlacementVerdictDto"/>. Gap 100 moves the sentences out to the front end.
/// </summary>
public sealed record RedemptionVerdictDto
{
    public const string ContractNotFound = "Contract not found.";

    public RedemptionPlacementOutcome Outcome { get; init; }
    public string? Message { get; init; }

    public static RedemptionVerdictDto From(RedemptionPlacementOutcome outcome) => new()
    {
        Outcome = outcome,
        Message = outcome switch
        {
            RedemptionPlacementOutcome.Placed => null,
            RedemptionPlacementOutcome.OutsideContract => "This time is outside the contract.",
            RedemptionPlacementOutcome.WrongDay => "This contract only lets you cover a deficit today.",
            RedemptionPlacementOutcome.NothingOwed => "Nothing of this preset is owed in this contract.",
            _ => null
        }
    };
}
