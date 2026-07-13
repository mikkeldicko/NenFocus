using FluentValidation;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.Seal;

public sealed class SealNenContractValidator : AbstractValidator<SealNenContractCommand>
{
    public SealNenContractValidator(IBlockPresetRepository presets)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Contract name is required.");

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("End date must be on or after the start date.");

        RuleFor(x => x.FixedDailyFloorHours)
            .InclusiveBetween(0, 24)
            .When(x => x.FixedDailyFloorHours.HasValue)
            .WithMessage("Daily floor must be between 0 and 24 hours.");

        RuleFor(x => x)
            .Must(x => x.WorkHoursStart.HasValue == x.WorkHoursEnd.HasValue)
            .WithMessage("Work hours start and end must both be set, or both left empty.");

        RuleFor(x => x.WorkHoursEnd)
            .GreaterThan(x => x.WorkHoursStart)
            .When(x => x.WorkHoursStart.HasValue && x.WorkHoursEnd.HasValue)
            .WithMessage("Work hours end must be after start.");

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
