using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckNenContractDateRangeOverlap;

namespace NenFocus.Application.UseCases.NenContracts.Seal;

public sealed class SealNenContractValidator : AbstractValidator<SealNenContractCommand>
{
    public SealNenContractValidator(
        IBlockPresetRepository presets,
        IQueryHandler<CheckNenContractDateRangeOverlapQuery, string?> checkOverlap)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Contract name is required.");

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("End date must be on or after the start date.");

        // The real guard — defense in depth behind the wizard's live Basics-step check,
        // which is UX convenience only and can't be trusted as the sole gate.
        RuleFor(x => x)
            .CustomAsync(async (command, context, ct) =>
            {
                if (command.EndDate < command.StartDate) return; // already reported above
                var overlapping = await checkOverlap.HandleAsync(
                    new CheckNenContractDateRangeOverlapQuery(command.StartDate, command.EndDate), ct);
                if (overlapping is not null)
                    context.AddFailure(nameof(SealNenContractCommand.StartDate),
                        $"This date range overlaps with your existing contract '{overlapping}'.");
            });

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
