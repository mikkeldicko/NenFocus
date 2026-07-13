using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.NenContracts.Extend;

public sealed class ExtendNenContractValidator : AbstractValidator<ExtendNenContractCommand>
{
    public ExtendNenContractValidator(INenContractRepository contracts, IBlockPresetRepository presets)
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
            .WithMessage("Only an active contract can be extended.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.ContractId == Guid.Empty) return true;
                var contract = await contracts.GetByIdAsync(command.ContractId, ct);
                return contract is null || command.NewEndDate > contract.EndDate;
            })
            .WithMessage("New end date must be after the contract's current end date.");

        RuleForEach(x => x.Blocks)
            .Must(b => b.End > b.Start)
            .WithMessage("Every block's end time must be after its start time.");

        RuleForEach(x => x.Blocks)
            .MustAsync(async (b, ct) =>
            {
                if (b.PresetId == Guid.Empty) return false;
                var preset = await presets.GetByIdAsync(b.PresetId, ct);
                return preset is not null;
            })
            .WithMessage("Every block must reference an existing preset.");
    }
}
