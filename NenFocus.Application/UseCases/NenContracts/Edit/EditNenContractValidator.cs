using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckNenContractDateRangeOverlap;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.NenContracts.Edit;

public sealed class EditNenContractValidator : AbstractValidator<EditNenContractCommand>
{
    public EditNenContractValidator(
        INenContractRepository contracts,
        IBlockPresetRepository presets,
        IQueryHandler<CheckNenContractDateRangeOverlapQuery, string?> checkOverlap)
    {
        RuleFor(x => x.ContractId)
            .NotEmpty().WithMessage("Contract ID is required.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.ContractId == Guid.Empty) return true;
                var contract = await contracts.GetByIdAsync(command.ContractId, ct);
                return contract is not null;
            })
            .WithMessage("Contract not found.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.ContractId == Guid.Empty) return true;
                var contract = await contracts.GetByIdAsync(command.ContractId, ct);
                return contract is null || contract.Status == NenContractStatus.Scheduled;
            })
            .WithMessage("Only a Scheduled contract can be edited — an Active contract has no edit path.");

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

        // See SealNenContractValidator for the full rationale — null means no fixed floor
        // deliberately; when present it must be a whole number, at least 1.
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

        RuleFor(x => x)
            .Must(x => x.FixedDailyFloorHours!.Value <= (x.WorkHoursEnd!.Value - x.WorkHoursStart!.Value).TotalHours)
            .When(x => x.FixedDailyFloorHours.HasValue && x.WorkHoursStart.HasValue && x.WorkHoursEnd.HasValue)
            .WithMessage("Daily floor cannot exceed the total work hours window.");

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
