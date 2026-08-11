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

        // Sealed-contract enforcement guards. A block's ContractId only ever resolves to a
        // contract that has been Seal()-ed — NenContract has no other way into existence —
        // so a resolvable contract IS a sealed contract; there is no separate "is it sealed"
        // flag to check. Only a still-live commitment (Scheduled or Active) constrains
        // edits — a Completed or Broken contract falls back to the ordinary HasEnded-based
        // protections, same precedent as the missing-contract case above.
        RuleFor(x => x)
            .CustomAsync(async (command, context, ct) =>
            {
                var block = await _blocks.GetByIdAsync(command.BlockId, ct);
                if (block?.ContractId is not { } contractId) return;

                var contract = await contracts.GetByIdAsync(contractId, ct);
                if (contract is null || !contract.IsInEffect) return;

                if (block.BlockPresetId != command.PresetId)
                {
                    context.AddFailure(nameof(UpdatePlannedBlockCommand.PresetId),
                        "This block's preset cannot be changed while it belongs to a sealed contract.");
                    return;
                }

                var originalDate = DateOnly.FromDateTime(block.Start.ToLocalTime());
                var newStartDate = DateOnly.FromDateTime(command.Start.ToLocalTime());
                var newEndDate = DateOnly.FromDateTime(command.End.ToLocalTime());

                if (contract.AllowsBlockTimeChange(originalDate, newStartDate, newEndDate))
                    return;

                var message = contract.StrictnessRule switch
                {
                    StrictnessRule.Locked => "This block is locked by a Lockdown contract.",
                    StrictnessRule.DailyFlexibility => "Daily Flex contracts only allow moving blocks within the same day.",
                    _ => "Cannot move block outside the contract period." // PeriodTimeBudget
                };
                context.AddFailure(nameof(UpdatePlannedBlockCommand.Start), message);
            });

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
