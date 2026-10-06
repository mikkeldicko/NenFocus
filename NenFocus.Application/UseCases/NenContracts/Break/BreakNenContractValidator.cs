using FluentValidation;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.Break;

public sealed class BreakNenContractValidator : AbstractValidator<BreakNenContractCommand>
{
    public BreakNenContractValidator(INenContractRepository contracts, ISessionRepository sessions)
    {
        RuleFor(x => x.ContractId)
            .NotEmpty().WithMessage("Contract ID is required.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.ContractId == Guid.Empty) return true;
                var contract = await contracts.GetByIdAsync(command.ContractId, ct);
                return contract is not null;
            })
            .WithMessage("Contract not found.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.ContractId == Guid.Empty) return true;
                var contract = await contracts.GetByIdAsync(command.ContractId, ct);
                return contract is null || contract.IsInEffect;
            })
            .WithMessage("Only a contract that is still in effect (Scheduled or Active) can be broken.");

        // The contract's own answer (NenContract.CheckBreak, Gap 95): no contract ends while a
        // session runs. AllowCancellation isn't a reason there yet (interim, 2026-08-18) — Gap 83
        // decides whether it or Strict Mode is the gate.
        RuleFor(x => x)
            .CustomAsync(async (command, context, ct) =>
            {
                var contract = await contracts.GetByIdAsync(command.ContractId, ct);
                if (contract is not { IsInEffect: true }) return; // reported by the rules above

                var verdict = contract.CheckBreak(sessionRunning: await sessions.GetActiveAsync(ct) is not null);
                if (ContractEndVerdictDto.From(verdict).Message is { } message)
                    context.AddFailure(nameof(BreakNenContractCommand.ContractId), message);
            });
    }
}
