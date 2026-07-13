using FluentValidation;

namespace NenFocus.Application.UseCases.ContractPresets.CreateContractPreset;

public sealed class CreateContractPresetValidator : AbstractValidator<CreateContractPresetCommand>
{
    public CreateContractPresetValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Preset name is required.");

        RuleFor(x => x.DailyFloorHours)
            .InclusiveBetween(0, 24).WithMessage("Daily floor must be between 0 and 24 hours.");

        RuleFor(x => x)
            .Must(x => x.WorkHoursStart.HasValue == x.WorkHoursEnd.HasValue)
            .WithMessage("Work hours start and end must both be set, or both left empty.");

        RuleFor(x => x.WorkHoursEnd)
            .GreaterThan(x => x.WorkHoursStart)
            .When(x => x.WorkHoursStart.HasValue && x.WorkHoursEnd.HasValue)
            .WithMessage("Work hours end must be after start.");
    }
}
