using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.NenContracts.Delete;

public sealed class DeleteNenContractValidator : AbstractValidator<DeleteNenContractCommand>
{
    public DeleteNenContractValidator(INenContractRepository contracts)
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
                return contract is null || contract.Status == NenContractStatus.Scheduled;
            })
            .WithMessage("Only a Scheduled contract can be deleted — an Active contract must be cancelled instead.");
    }
}
