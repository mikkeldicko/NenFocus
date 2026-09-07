using FluentValidation;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractCompletionReadiness;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.NenContracts.CompleteNenContract;

public sealed class CompleteNenContractValidator : AbstractValidator<CompleteNenContractCommand>
{
    public CompleteNenContractValidator(
        INenContractRepository contracts,
        IQueryHandler<GetNenContractCompletionReadinessQuery, NenContractCompletionReadinessDto> readiness)
    {
        RuleFor(x => x.ContractId)
            .MustAsync(async (id, ct) =>
            {
                var contract = await contracts.GetByIdAsync(id, ct);
                return contract is not null;
            })
            .WithMessage("Contract not found.");

        RuleFor(x => x.ContractId)
            .MustAsync(async (id, ct) =>
            {
                var contract = await contracts.GetByIdAsync(id, ct);
                return contract is null || contract.Status == NenContractStatus.Active;
            })
            .WithMessage("Only an active contract can be completed.");

        // The real gate: every committed hour delivered, and nothing left pending — no
        // still-open block (committed, redemption, or Bonus) anywhere in the contract's
        // own window. The second half isn't just conservative: it's what guarantees
        // completing here never needs Break's cleanup logic, because by construction
        // there's nothing left to clean up. Delegates to GetNenContractCompletionReadinessQuery
        // — the same query the Planner's "Complete Contract" button binds to directly to
        // decide its own visibility, so the rule that gates the command and the rule that
        // decides whether to show the button can never drift apart.
        RuleFor(x => x)
            .CustomAsync(async (command, context, ct) =>
            {
                var contractExists = await contracts.GetByIdAsync(command.ContractId, ct) is not null;
                if (!contractExists) return; // already reported by the "not found" rule above

                var result = await readiness.HandleAsync(
                    new GetNenContractCompletionReadinessQuery(command.ContractId), ct);
                if (result.IsReady) return;

                if (result.RemainingHours > 0)
                    context.AddFailure(nameof(CompleteNenContractCommand.ContractId),
                        $"You haven't completed all committed hours yet ({result.RemainingHours:0.#}h remaining).");
                else if (result.PendingBlockCount > 0)
                    context.AddFailure(nameof(CompleteNenContractCommand.ContractId),
                        "There's still a block scheduled in this contract's window — move or remove it before completing.");
            });
    }
}
