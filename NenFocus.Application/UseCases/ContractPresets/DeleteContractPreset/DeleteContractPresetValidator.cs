using FluentValidation;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.ContractPresets.DeleteContractPreset;

public sealed class DeleteContractPresetValidator : AbstractValidator<DeleteContractPresetCommand>
{
    public DeleteContractPresetValidator(INenContractRepository contracts)
    {
        RuleFor(x => x.PresetId)
            .MustAsync(async (id, ct) => !await contracts.HasContractsInEffectForPresetAsync(id, ct))
            .WithMessage("This preset is currently in effect for an active or scheduled Nen Contract and cannot be deleted.");
    }
}
