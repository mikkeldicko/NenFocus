using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckMakeUpForEntry;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed class CreatePlannedBlockValidator : AbstractValidator<CreatePlannedBlockCommand>
{
    public CreatePlannedBlockValidator(
        IBlockPresetRepository presets,
        INenContractRepository contracts,
        ISystemClock clock,
        IQueryHandler<CheckMakeUpForEntryQuery, MakeUpForEntryCheckResult> checkMakeUpForEntry)
    {
        RuleFor(x => x.PresetId)
            .NotEmpty().WithMessage("Preset ID is required.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.PresetId == Guid.Empty) return true;
                var preset = await presets.GetByIdAsync(command.PresetId, ct);
                return preset is null || preset.PresetType == command.BlockPresetType;
            })
            .WithMessage(x =>
                $"A {x.BlockPresetType.ToString().ToLowerInvariant()} block requires a {x.BlockPresetType.ToString().ToLowerInvariant()} preset.");

        RuleFor(x => x.Start)
            .Must(start => start > clock.UtcNow)
            .WithMessage("Start time must be in the future.");

        RuleFor(x => x.End)
            .GreaterThan(x => x.Start)
            .WithMessage("End time must be after start time.")
            .Must((command, end) => (end - command.Start).TotalMinutes >= 1)
            .WithMessage("Block must be at least 1 minute long.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.ContractId is not { } contractId) return true;
                var contract = await contracts.GetByIdAsync(contractId, ct);
                return contract is null
                    || contract.AllowsBlockPlacement(DateOnly.FromDateTime(command.Start), command.Start, command.End);
            })
            .WithMessage("This block falls outside the contract's configured Work Hours for that day.");

        RuleFor(x => x.MakeUpForEntries)
            .Empty()
            .When(x => x.BlockPresetType != BlockPresetType.Focus)
            .WithMessage("Only a Focus block can cover deficit for missed blocks — Background blocks have no sessions.");

        RuleForEach(x => x.MakeUpForEntries)
            .CustomAsync(async (entry, context, ct) =>
            {
                var command = context.InstanceToValidate;
                if (command.BlockPresetType != BlockPresetType.Focus) return; // already reported above

                var result = await checkMakeUpForEntry.HandleAsync(
                    new CheckMakeUpForEntryQuery(command.PresetId, command.Start, entry.MissedBlockId, entry.MinutesCovered, ExcludeBlockId: null),
                    ct);

                var message = MakeUpForEntryMessages.For(result);
                if (message is not null)
                    context.AddFailure(nameof(CreatePlannedBlockCommand.MakeUpForEntries), message);
            });
    }
}
