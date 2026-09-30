using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckNenContractDateRangeOverlap;

namespace NenFocus.Application.UseCases.NenContracts.Seal;

public sealed class SealNenContractValidator : AbstractValidator<SealNenContractCommand>
{
    public SealNenContractValidator(
        IBlockPresetRepository presets,
        IContractPresetRepository contractPresets,
        IQueryHandler<CheckNenContractDateRangeOverlapQuery, string?> checkOverlap)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Contract name is required.");

        // Defense in depth behind the wizard's own guard (ContractRulesStepViewModel.Continue
        // already refuses to proceed in Advanced mode without a preset picked) — this is the
        // real gate for whatever actually reaches the command, the same relationship the
        // date-range-overlap check above already has to its own wizard-side live check. Null
        // is untouched: a Simple contract legitimately has no preset at all.
        RuleFor(x => x.ContractPresetId)
            .MustAsync(async (id, ct) => id is null || await contractPresets.GetByIdAsync(id.Value, ct) is not null)
            .WithMessage("The selected contract preset no longer exists — please pick another.");

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

        // Null means no fixed floor — a deliberate, unambiguous choice. When present it
        // must be a whole number and at least 1: zero would mean "no floor" by another
        // name, reintroducing the exact 0-vs-blank confusion leaving it unset already avoids.
        RuleFor(x => x.FixedDailyFloorHours)
            .Must(f => f % 1 == 0)
            .When(x => x.FixedDailyFloorHours.HasValue)
            .WithMessage("Daily floor must be a whole number of hours.");

        RuleFor(x => x.FixedDailyFloorHours)
            .InclusiveBetween(1, 24)
            .When(x => x.FixedDailyFloorHours.HasValue)
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
            .Must(x => x.FixedDailyFloorHours!.Value <= (x.WorkHoursEnd!.Value - x.WorkHoursStart!.Value).TotalHours)
            .When(x => x.FixedDailyFloorHours.HasValue && x.WorkHoursStart.HasValue && x.WorkHoursEnd.HasValue)
            .WithMessage("Daily floor cannot exceed the total work hours window.");

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
