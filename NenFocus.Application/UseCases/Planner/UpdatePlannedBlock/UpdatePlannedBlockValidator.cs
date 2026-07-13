using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckMakeUpForEntry;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;

public sealed class UpdatePlannedBlockValidator : AbstractValidator<UpdatePlannedBlockCommand>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISystemClock _clock;

    public UpdatePlannedBlockValidator(
        IPlannedBlockRepository blocks,
        IBlockPresetRepository presets,
        INenContractRepository contracts,
        ISystemClock clock,
        IQueryHandler<CheckMakeUpForEntryQuery, MakeUpForEntryCheckResult> checkMakeUpForEntry)
    {
        _blocks = blocks;
        _clock = clock;

        RuleFor(x => x.BlockId)
            .NotEmpty().WithMessage("Block ID is required.");

        RuleFor(x => x.PresetId)
            .NotEmpty().WithMessage("Preset ID is required.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.PresetId == Guid.Empty || command.BlockId == Guid.Empty) return true;
                var block   = await _blocks.GetByIdAsync(command.BlockId, ct);
                var preset = await presets.GetByIdAsync(command.PresetId, ct);
                if (block is null || preset is null) return true;
                var required = block is FocusBlock ? BlockPresetType.Focus : BlockPresetType.Background;
                return preset.PresetType == required;
            })
            .WithMessage("The selected preset type does not match this block preset type.");

        // If the block has already started its start time is locked — the UI greys it
        // out and sends the original value. Skip the future check in that case so a
        // valid end-time-only edit isn't rejected by the past start time.
        RuleFor(x => x.Start)
            .MustAsync(async (command, start, ct) =>
            {
                var existing = await _blocks.GetByIdAsync(command.BlockId, ct);
                if (existing is not null && existing.Start <= _clock.UtcNow)
                    return true;
                return start > _clock.UtcNow;
            })
            .WithMessage("Start time must be in the future.");

        RuleFor(x => x.End)
            .GreaterThan(x => x.Start)
            .WithMessage("End time must be after start time.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.MakeUpForEntries.Count == 0) return true;
                var block = await _blocks.GetByIdAsync(command.BlockId, ct);
                return block is FocusBlock;
            })
            .WithMessage("Only a Focus block can cover deficit for missed blocks — Background blocks have no sessions.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var block = await _blocks.GetByIdAsync(command.BlockId, ct);
                if (block?.ContractId is not { } contractId) return true;
                var contract = await contracts.GetByIdAsync(contractId, ct);
                return contract is null
                    || contract.AllowsBlockPlacement(DateOnly.FromDateTime(command.Start), command.Start, command.End);
            })
            .WithMessage("This block falls outside the contract's configured Work Hours for that day.");

        RuleForEach(x => x.MakeUpForEntries)
            .CustomAsync(async (entry, context, ct) =>
            {
                var command = context.InstanceToValidate;
                var block = await _blocks.GetByIdAsync(command.BlockId, ct);
                if (block is not FocusBlock) return; // already reported above

                var result = await checkMakeUpForEntry.HandleAsync(
                    new CheckMakeUpForEntryQuery(command.PresetId, command.Start, entry.MissedBlockId, entry.MinutesCovered, command.BlockId),
                    ct);

                var message = MakeUpForEntryMessages.For(result);
                if (message is not null)
                    context.AddFailure(nameof(UpdatePlannedBlockCommand.MakeUpForEntries), message);
            });
    }
}
