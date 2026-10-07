using FluentValidation;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

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

        // The contract's own answer (NenContract.CheckDelete, Gap 92): only before it starts.
        RuleFor(x => x)
            .CustomAsync(async (command, context, ct) =>
            {
                if (command.ContractId == Guid.Empty) return;
                if (await contracts.GetByIdAsync(command.ContractId, ct) is not { } contract) return; // reported above

                if (ContractActionVerdictDto.From(contract.CheckDelete()).Message is { } message)
                    context.AddFailure(nameof(DeleteNenContractCommand.ContractId), message);
            });
    }
}
