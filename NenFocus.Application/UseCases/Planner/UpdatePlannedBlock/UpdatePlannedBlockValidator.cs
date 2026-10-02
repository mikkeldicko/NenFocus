using FluentValidation;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetBlockPlacementVerdict;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

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
        IBlockChangeChecker changes,
        IQueryHandler<GetBlockPlacementVerdictQuery, BlockPlacementVerdictDto> placement)
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
                var required = block.PresetType;
                return preset.PresetType == required;
            })
            .WithMessage("The selected preset type does not match this block preset type.");

        // Mirrors FocusBlock.Reschedule's own cap — the entity is the real,
        // unbypassable invariant; this exists only to produce a friendly FormError
        // instead of an unhandled exception, same two-layer pattern already used for
        // end <= start. An Update can never change a block between Focus and
        // Background (enforced by the preset-type-match rule above), so checking the
        // existing block's type is reliable here.
        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var block = await _blocks.GetByIdAsync(command.BlockId, ct);
                return block is not FocusBlock || command.Slot.Length <= TimeSpan.FromHours(FocusBlock.MaxDurationHours);
            })
            .WithMessage($"A Focus block cannot exceed {FocusBlock.MaxDurationHours} hours.");

        // May this block change at all, and in this way? The one permission answer,
        // BlockChangeChecker (Gap 93): the block's own lock, then its contract's rules, then the
        // floor. Asked before placement, so a running block answers "it has started", not "that
        // time is in the past". Then the one placement rule (Gap 88 phase 8): not in the past →
        // inside the contract's window → free for Focus.
        RuleFor(x => x)
            .CustomAsync(async (command, context, ct) =>
            {
                if (command.BlockId == Guid.Empty) return;
                var block = await _blocks.GetByIdAsync(command.BlockId, ct);
                if (block is null) return;

                NenContract? contract = null;
                IReadOnlyList<PlannedBlock> contractBlocks = [];
                if (block.Membership is { } membership)
                {
                    contract = await contracts.GetByIdAsync(membership.ContractId, ct);
                    contractBlocks = await _blocks.GetForContractAsync(membership.ContractId, ct);
                }

                var change = changes.CheckReschedule(block, command.Slot, block.BlockPresetId != command.PresetId,
                    contract, contractBlocks, _clock.Now, _clock.TimeZone);
                if (!change.IsAllowed)
                {
                    var property = change.Outcome == ChangeOutcome.PresetFixed
                        ? nameof(UpdatePlannedBlockCommand.PresetId)
                        : nameof(UpdatePlannedBlockCommand.Slot);
                    context.AddFailure(property, BlockChangeVerdictDto.From(change).Message);
                    return;
                }

                var verdict = await placement.HandleAsync(
                    new GetBlockPlacementVerdictQuery(command.Slot, block.PresetType, command.PresetId, ExistingBlockId: command.BlockId),
                    ct);
                if (verdict.Outcome != PlacementOutcome.Allowed && verdict.Message is not null)
                    context.AddFailure(nameof(UpdatePlannedBlockCommand.Slot), verdict.Message);
            });
    }
}
