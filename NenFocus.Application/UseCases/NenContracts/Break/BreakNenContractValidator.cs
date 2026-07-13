using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.NenContracts.Break;

public sealed class BreakNenContractValidator : AbstractValidator<BreakNenContractCommand>
{
    public BreakNenContractValidator(INenContractRepository contracts)
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
                return contract is null || contract.Status == NenContractStatus.Active;
            })
            .WithMessage("Only an active contract can be broken.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.ContractId == Guid.Empty) return true;
                var contract = await contracts.GetByIdAsync(command.ContractId, ct);
                return contract is null || contract.AllowCancellation;
            })
            .WithMessage("This contract does not allow cancellation.");
    }
}
