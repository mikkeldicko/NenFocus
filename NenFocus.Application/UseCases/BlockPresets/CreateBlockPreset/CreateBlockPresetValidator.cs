using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Constants;

namespace NenFocus.Application.UseCases.BlockPresets.CreateBlockPreset;

public sealed class CreateBlockPresetValidator : AbstractValidator<CreateBlockPresetCommand>
{
    public CreateBlockPresetValidator(IBlockPresetRepository presets)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Preset name is required.");

        RuleFor(x => x)
            .Must(x => x.BlockedApps.Count > 0 || x.Websites.Count > 0)
            .WithMessage("A preset must have at least one blocked app or website.");

        RuleForEach(x => x.BlockedApps)
            .Must(app => !SystemSafetyConstants.UnkillableProcesses.Contains(ProcessUtils.NormalizeProcessName(app)))
            .WithMessage(app => $"'{app}' is a critical system process and cannot be blocked.");

        RuleForEach(x => x.Tags)
            .MaximumLength(30).WithMessage("Tags cannot exceed 30 characters.");
    }
}