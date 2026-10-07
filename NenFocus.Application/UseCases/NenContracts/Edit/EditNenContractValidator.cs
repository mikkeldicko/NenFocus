using FluentValidation;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckNenContractDateRangeOverlap;

namespace NenFocus.Application.UseCases.NenContracts.Edit;

public sealed class EditNenContractValidator : AbstractValidator<EditNenContractCommand>
{
    public EditNenContractValidator(
        INenContractRepository contracts,
        IBlockPresetRepository presets,
        IContractPresetRepository contractPresets,
        IQueryHandler<CheckNenContractDateRangeOverlapQuery, string?> checkOverlap)
    {
        RuleFor(x => x.ContractId)
            .NotEmpty().WithMessage("Contract ID is required.");

        // See SealNenContractValidator for the full rationale — defense in depth behind the
        // wizard's own guard; null is untouched (a Simple contract legitimately has none).
        RuleFor(x => x.ContractPresetId)
            .MustAsync(async (id, ct) => id is null || await contractPresets.GetByIdAsync(id.Value, ct) is not null)
            .WithMessage("The selected contract preset no longer exists — please pick another.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.ContractId == Guid.Empty) return true;
                var contract = await contracts.GetByIdAsync(command.ContractId, ct);
                return contract is not null;
            })
            .WithMessage("Contract not found.");

        // The contract's own answer (NenContract.CheckEdit, Gap 92): only before it starts.
        RuleFor(x => x)
            .CustomAsync(async (command, context, ct) =>
            {
                if (command.ContractId == Guid.Empty) return;
                if (await contracts.GetByIdAsync(command.ContractId, ct) is not { } contract) return; // reported above

                if (ContractActionVerdictDto.From(contract.CheckEdit()).Message is { } message)
                    context.AddFailure(nameof(EditNenContractCommand.ContractId), message);
            });

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Contract name is required.");

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("End date must be on or after the start date.");

        // Excludes the contract being edited — its own not-yet-deleted prior blocks would
        // otherwise show up as a false self-collision. Same defense-in-depth relationship
        // to the wizard's live Basics-step check that SealNenContractValidator has.
        RuleFor(x => x)
            .CustomAsync(async (command, context, ct) =>
            {
                if (command.EndDate < command.StartDate) return; // already reported above
                var overlapping = await checkOverlap.HandleAsync(
                    new CheckNenContractDateRangeOverlapQuery(command.StartDate, command.EndDate, command.ContractId), ct);
                if (overlapping is not null)
                    context.AddFailure(nameof(EditNenContractCommand.StartDate),
                        $"This date range overlaps with your existing contract '{overlapping}'.");
            });

        // One question to the Domain (ContractPreset.CheckSettings, Gap 92 V9): a whole-hour floor
        // from 1 to 24 that fits the Work Hours. "End after start" and "both or neither" can't be
        // sent: the command carries a WorkHours.
        RuleFor(x => x)
            .Custom((command, context) =>
            {
                if (ContractSettingsVerdictDto.Check(command.FixedDailyFloorHours, command.WorkHours).Message is { } message)
                    context.AddFailure(nameof(EditNenContractCommand.FixedDailyFloorHours), message);
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
