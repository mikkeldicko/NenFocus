using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.DTOs;

/// <summary>
/// The answer to "may this contract end now?" as validators and the UI read it — the outcome,
/// plus the one user-facing sentence (Gap 95). The sibling of BlockChangeVerdictDto.
/// </summary>
public sealed record ContractEndVerdictDto
{
    public ContractEndOutcome Outcome { get; init; }
    public string? Message { get; init; }

    /// <summary>
    /// The one builder, and the one home of the contract-end wording: the Complete and Break
    /// validators and handlers and the readiness query all go through it. Gap 100 moves the
    /// sentences out to the front end.
    /// </summary>
    public static ContractEndVerdictDto From(ContractEndVerdict verdict) => new()
    {
        Outcome = verdict.Outcome,
        Message = verdict.Outcome switch
        {
            ContractEndOutcome.Allowed => null,
            ContractEndOutcome.SessionIsRunning =>
                "A session is running — a contract can't be ended until it finishes.",
            ContractEndOutcome.HoursNotDelivered =>
                $"You haven't completed all committed hours yet ({verdict.RemainingHours:0.#}h remaining).",
            ContractEndOutcome.BlocksStillPending =>
                "There's still a block scheduled in this contract's window — move or remove it before completing.",
            _ => null
        }
    };
}
