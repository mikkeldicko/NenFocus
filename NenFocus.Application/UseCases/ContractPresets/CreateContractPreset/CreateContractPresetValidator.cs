using FluentValidation;

namespace NenFocus.Application.UseCases.ContractPresets.CreateContractPreset;

public sealed class CreateContractPresetValidator : AbstractValidator<CreateContractPresetCommand>
{
    public CreateContractPresetValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Preset name is required.");

        // Null means no fixed floor — a legitimate, deliberate choice, not a missing value.
        // When present, it must be a whole number and at least 1: zero would mean "no
        // floor" by another name, and this codebase already has an unambiguous way to say
        // that (leave it unset) — allowing zero here would just reintroduce the confusion.
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

        // A floor that can never be satisfied within the enforced work hours window makes
        // no sense to allow — e.g. a 5h window can never fit a 6h daily floor.
        RuleFor(x => x)
            .Must(x => x.DailyFloorHours.Value <= (x.WorkHoursEnd!.Value - x.WorkHoursStart!.Value).TotalHours)
            .When(x => x.DailyFloorHours.HasValue && x.WorkHoursStart.HasValue && x.WorkHoursEnd.HasValue)
            .WithMessage("Daily floor cannot exceed the total work hours window.");
    }
}
