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

        // The contract's own answer (NenContract.CheckBreak, Gap 92): still in effect, sealed with
        // cancellation on, and no session running.
        RuleFor(x => x)
            .CustomAsync(async (command, context, ct) =>
            {
                var contract = await contracts.GetByIdAsync(command.ContractId, ct);
                if (contract is null) return; // reported by the rules above

                var verdict = contract.CheckBreak(sessionRunning: await sessions.GetActiveAsync(ct) is not null);
                if (ContractActionVerdictDto.From(verdict).Message is { } message)
                    context.AddFailure(nameof(BreakNenContractCommand.ContractId), message);
            });
    }
}
