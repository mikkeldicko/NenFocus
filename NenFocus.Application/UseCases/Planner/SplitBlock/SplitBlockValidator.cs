using FluentValidation;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Planner.SplitBlock;

public sealed class SplitBlockValidator : AbstractValidator<SplitBlockCommand>
{
    public SplitBlockValidator(
        IBlockRepository blocks,
        ISessionRepository sessions,
        INenContractRepository contracts,
        ISystemClock clock,
        BlockChangeChecker changes,
        BlockPlacementChecker placement)
    {
        RuleFor(x => x.OriginalBlockId)
            .NotEmpty().WithMessage("Original block ID is required.");

        // The slots are required (the compiler enforces it) and guarantee end after start
        // (Gap 88) — the split dialog shows "First block's end time must be after its start
        // time." itself when TimeSlot.TryCreate returns null, so there's no such rule here.

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.OriginalBlockId == Guid.Empty) return true;
                var block = await blocks.GetByIdAsync(command.OriginalBlockId, ct);
                return block is not null;
            })
            .WithMessage("The block to split could not be found.");

        // The shape of a split: two pieces that don't overlap, that together are exactly as long
        // as the original, each on a single day (the dialog has no date-rollover for one piece).
        // SplitByDay is half-open: a piece ending exactly at midnight has no part on the next day.
        RuleFor(x => x)
            .Must(command => !command.First.Overlaps(command.Second))
            .WithMessage("The two resulting blocks cannot overlap each other.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var block = await blocks.GetByIdAsync(command.OriginalBlockId, ct);
                if (block is null) return true; // reported above
                return command.First.Length + command.Second.Length == block.Scheduled.Length;
            })
            .WithMessage("The two resulting blocks must add up to the original block's total duration.");

        RuleFor(x => x.First)
            .Must(slot => clock.TimeZone.SplitByDay(slot).Count() == 1)
            .WithMessage("The first block must land entirely on one day.");

        RuleFor(x => x.Second)
            .Must(slot => clock.TimeZone.SplitByDay(slot).Count() == 1)
            .WithMessage("The second block must land entirely on one day.");

        // May this block be split, and into these two pieces? The one permission answer,
        // BlockChangeChecker (Gap 93): the block's own lock, whether its contract offers a split,
        // where each piece may land, and the floor with both in place. Then, for each piece, the
        // one placement answer, BlockPlacementChecker.Check (Gap 88 phase 8): not in the past →
        // inside the contract's hours → free for Focus, against the calendar as it will be
        // (without the original) and the running session. Placement is asked directly rather than
        // through GetBlockPlacementVerdictQuery, which would run redemption attribution per piece
        // on its own — Split divides the original's coverage across both (Gap 106).
        RuleFor(x => x)
            .CustomAsync(async (command, context, ct) =>
            {
                var block = await blocks.GetByIdAsync(command.OriginalBlockId, ct);
                if (block is null) return; // reported above

                NenContract? contract = null;
                IReadOnlyList<Block> contractBlocks = [];
                if (block.Membership is { } membership)
                {
                    contract = await contracts.GetByIdAsync(membership.ContractId, ct);
                    contractBlocks = await blocks.GetForContractAsync(membership.ContractId, ct);
                }

                var change = changes.CheckSplit(block, command.First, command.Second,
                    contract, contractBlocks, clock.Now, clock.TimeZone);
                if (!change.IsAllowed)
                {
                    context.AddFailure(nameof(SplitBlockCommand.Second), BlockChangeVerdictDto.From(change).Message);
                    return;
                }

                var contractsInEffect = await contracts.GetAllInEffectAsync(ct);
                var running = await sessions.GetActiveAsync(ct);
                foreach (var (slot, property, label) in new[]
                {
                    (command.First, nameof(SplitBlockCommand.First), "First block"),
                    (command.Second, nameof(SplitBlockCommand.Second), "Second block")
                })
                {
                    var calendar = (await blocks.GetForRangeAsync(slot, ct)).Where(b => b.Id != command.OriginalBlockId);
                    var verdict = placement.Check(slot, BlockPresetType.Focus, block.Membership, contractsInEffect, calendar, running, clock.Now, clock.TimeZone);
                    if (verdict.Outcome != PlacementOutcome.Allowed)
                        context.AddFailure(property, $"{label}: {BlockPlacementVerdictDto.From(verdict).Message}");
                }
            });
    }
}
