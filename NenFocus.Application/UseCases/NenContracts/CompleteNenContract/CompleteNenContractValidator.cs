using FluentValidation;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractCompletionReadiness;

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

        // The gate is the contract's own answer (NenContract.CheckComplete, Gaps 95, 92): Active, no
        // session running, every committed hour delivered, every block done — read through
        // GetNenContractCompletionReadinessQuery, the same answer the Planner's "Complete
        // Contract" button binds to, so the gate and the button can never drift apart.
        RuleFor(x => x)
            .CustomAsync(async (command, context, ct) =>
            {
                var contract = await contracts.GetByIdAsync(command.ContractId, ct);
                if (contract is null) return; // reported by the rule above

                var result = await readiness.HandleAsync(
                    new GetNenContractCompletionReadinessQuery(command.ContractId), ct);
                if (!result.IsReady && result.NotReadyReason is { } reason)
                    context.AddFailure(nameof(CompleteNenContractCommand.ContractId), reason);
            });
    }
}
