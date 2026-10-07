using FluentValidation;
using NenFocus.Application.DTOs;
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

        // One question to the Domain (ContractPreset.CheckSettings, Gap 92 V9): a whole-hour floor
        // from 1 to 24 that fits the Work Hours. "End after start" and "both or neither" can't be
        // sent: the command carries a WorkHours.
        RuleFor(x => x)
            .Custom((command, context) =>
            {
                if (ContractSettingsVerdictDto.Check(command.FixedDailyFloorHours, command.WorkHours).Message is { } message)
                    context.AddFailure(nameof(SealNenContractCommand.FixedDailyFloorHours), message);
            });

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
