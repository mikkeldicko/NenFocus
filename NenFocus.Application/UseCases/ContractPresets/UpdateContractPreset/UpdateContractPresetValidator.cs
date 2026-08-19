using FluentValidation;

namespace NenFocus.Application.UseCases.ContractPresets.UpdateContractPreset;

public sealed class UpdateContractPresetValidator : AbstractValidator<UpdateContractPresetCommand>
{
    public UpdateContractPresetValidator()
    {
        RuleFor(x => x.PresetId)
            .NotEmpty().WithMessage("Preset ID is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Preset name is required.");

        // See CreateContractPresetValidator for the full rationale — null means no fixed
        // floor deliberately; when present it must be a whole number, at least 1.
        RuleFor(x => x.DailyFloorHours)
            .Must(f => f % 1 == 0)
            .When(x => x.DailyFloorHours.HasValue)
            .WithMessage("Daily floor must be a whole number of hours.");

        RuleFor(x => x.DailyFloorHours)
            .InclusiveBetween(1, 24)
            .When(x => x.DailyFloorHours.HasValue)
            .WithMessage("Daily floor must be between 1 and 24 hours.");

        RuleFor(x => x)
            .Must(x => x.WorkHoursStart.HasValue == x.WorkHoursEnd.HasValue)
            .WithMessage("Work hours start and end must both be set, or both left empty.");

        RuleFor(x => x.WorkHoursEnd)
            .GreaterThan(x => x.WorkHoursStart)
            .When(x => x.WorkHoursStart.HasValue && x.WorkHoursEnd.HasValue)
            .WithMessage("Work hours end must be after start.");

        RuleFor(x => x)
            .Must(x => x.DailyFloorHours!.Value <= (x.WorkHoursEnd!.Value - x.WorkHoursStart!.Value).TotalHours)
            .When(x => x.DailyFloorHours.HasValue && x.WorkHoursStart.HasValue && x.WorkHoursEnd.HasValue)
            .WithMessage("Daily floor cannot exceed the total work hours window.");
    }
}
