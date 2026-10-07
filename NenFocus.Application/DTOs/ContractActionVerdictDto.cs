using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.DTOs;

/// <summary>
/// The answer to "may this lifecycle action happen to the contract now?" as validators and the UI
/// read it — the outcome, plus the one user-facing sentence (Gaps 95, 92). The sibling of
/// BlockChangeVerdictDto.
/// </summary>
public sealed record ContractActionVerdictDto
{
    public ContractActionOutcome Outcome { get; init; }
    public string? Message { get; init; }

    public bool IsAllowed => Outcome == ContractActionOutcome.Allowed;

    /// <summary>
    /// The one builder, and the one home of the contract-lifecycle wording: the Edit, Delete,
    /// Complete and Break validators and handlers and the readiness query all go through it.
    /// Gap 100 moves the sentences out to the front end.
    /// </summary>
    public static ContractActionVerdictDto From(ContractActionVerdict verdict) => new()
    {
        Outcome = verdict.Outcome,
        Message = verdict.Outcome switch
        {
            ContractActionOutcome.Allowed => null,
            ContractActionOutcome.NotScheduled =>
                "Only an upcoming contract can be edited or deleted — once it has started it can only be completed or cancelled.",
            ContractActionOutcome.NotActive =>
                "Only an active contract can be completed.",
            ContractActionOutcome.NotInEffect =>
                "This contract has already ended — it can't be cancelled.",
            ContractActionOutcome.CancellationNotAllowed =>
                "This contract was sealed without cancellation — it runs until its end.",
            ContractActionOutcome.SessionIsRunning =>
                "A session is running — a contract can't be ended until it finishes.",
            ContractActionOutcome.HoursNotDelivered =>
                $"You haven't completed all committed hours yet ({verdict.RemainingHours:0.#}h remaining).",
            ContractActionOutcome.BlocksStillPending =>
                "There's still a block scheduled in this contract's window — move or remove it before completing.",
            _ => null
        }
    };
}
