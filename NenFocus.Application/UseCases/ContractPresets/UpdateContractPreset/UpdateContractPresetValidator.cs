using FluentValidation;
using NenFocus.Application.DTOs;

namespace NenFocus.Application.UseCases.ContractPresets.UpdateContractPreset;

public sealed class UpdateContractPresetValidator : AbstractValidator<UpdateContractPresetCommand>
{
    public UpdateContractPresetValidator()
    {
        RuleFor(x => x.PresetId)
            .NotEmpty().WithMessage("Preset ID is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Preset name is required.");

        // One question to the Domain (ContractPreset.CheckSettings, Gap 92 V9): a whole-hour floor
        // from 1 to 24 that fits the Work Hours. "End after start" and "both or neither" can't be
        // sent: the command carries a WorkHours.
        RuleFor(x => x)
            .Custom((command, context) =>
            {
                if (ContractSettingsVerdictDto.Check(command.DailyFloorHours, command.WorkHours).Message is { } message)
                    context.AddFailure(nameof(UpdateContractPresetCommand.DailyFloorHours), message);
            });
    }
}
