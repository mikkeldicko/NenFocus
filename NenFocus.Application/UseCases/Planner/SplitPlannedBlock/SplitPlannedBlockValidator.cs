using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Planner.SplitPlannedBlock;

public sealed class SplitPlannedBlockValidator : AbstractValidator<SplitPlannedBlockCommand>
{
    public SplitPlannedBlockValidator(
        IPlannedBlockRepository blocks,
        ISessionRepository sessions,
        INenContractRepository contracts,
        ISystemClock clock)
    {
        RuleFor(x => x.OriginalBlockId)
            .NotEmpty().WithMessage("Original block ID is required.");

        RuleFor(x => x.FirstBlockEnd)
            .GreaterThan(x => x.FirstBlockStart)
            .WithMessage("First block's end time must be after its start time.");

        RuleFor(x => x.SecondBlockEnd)
            .GreaterThan(x => x.SecondBlockStart)
            .WithMessage("Second block's end time must be after its start time.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.OriginalBlockId == Guid.Empty) return true;
                var block = await blocks.GetByIdAsync(command.OriginalBlockId, ct);
                return block is not null;
            })
            .WithMessage("The block to split could not be found.");

        // Split is exclusively a sealed-contract feature — an ordinary block has no
        // strictness rule to check and isn't in scope for this operation.
        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var block = await blocks.GetByIdAsync(command.OriginalBlockId, ct);
                return block?.ContractId is not null;
            })
            .WithMessage("Only a block belonging to a sealed contract can be split.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var block = await blocks.GetByIdAsync(command.OriginalBlockId, ct);
                if (block?.ContractId is not { } contractId) return true; // reported above
                var contract = await contracts.GetByIdAsync(contractId, ct);
                return contract is not null && contract.StrictnessRule == StrictnessRule.DailyFlexibility;
            })
            .WithMessage("Only a Daily Flexibility contract allows splitting a block — Lockdown blocks are immutable.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var block = await blocks.GetByIdAsync(command.OriginalBlockId, ct);
                return block is null || block.CancelledAt is null;
            })
            .WithMessage("This block was cancelled and cannot be split.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.OriginalBlockId == Guid.Empty) return true;
                var session = await sessions.GetByOriginatingBlockIdAsync(command.OriginalBlockId, ct);
                return session is null;
            })
            .WithMessage("A block with a session already run against it cannot be split.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var block = await blocks.GetByIdAsync(command.OriginalBlockId, ct);
                return block is not null && block.Start > clock.UtcNow;
            })
            .WithMessage("Only a future block can be split.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var block = await blocks.GetByIdAsync(command.OriginalBlockId, ct);
                if (block is null) return true; // reported above
                var originalDate = DateOnly.FromDateTime(block.Start.ToLocalTime());
                var firstStartDate = DateOnly.FromDateTime(command.FirstBlockStart.ToLocalTime());
                // Exact-midnight end (e.g. ...-24:00) has no presence on the next day —
                // see UpdatePlannedBlockValidator's identical fix for the full rationale.
                var firstEndDate = DateOnly.FromDateTime(command.FirstBlockEnd.ToLocalTime().AddTicks(-1));
                return firstStartDate == originalDate && firstEndDate == originalDate;
            })
            .WithMessage("The first block must stay on the same day as the original block.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var block = await blocks.GetByIdAsync(command.OriginalBlockId, ct);
                if (block is null) return true; // reported above
                var originalDate = DateOnly.FromDateTime(block.Start.ToLocalTime());
                var secondStartDate = DateOnly.FromDateTime(command.SecondBlockStart.ToLocalTime());
                var secondEndDate = DateOnly.FromDateTime(command.SecondBlockEnd.ToLocalTime().AddTicks(-1));
                return secondStartDate == originalDate && secondEndDate == originalDate;
            })
            .WithMessage("The second block must stay on the same day as the original block.");

        RuleFor(x => x)
            .Must(command => command.FirstBlockStart >= command.SecondBlockEnd || command.SecondBlockStart >= command.FirstBlockEnd)
            .WithMessage("The two resulting blocks cannot overlap each other.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var block = await blocks.GetByIdAsync(command.OriginalBlockId, ct);
                if (block is null) return true; // reported above
                var overlapping = await blocks.GetOverlappingAsync(
                    command.FirstBlockStart, command.FirstBlockEnd, clock.UtcNow,
                    ritualOnly: block is FocusBlock, excludeId: command.OriginalBlockId, cancellationToken: ct);
                return overlapping.Count == 0;
            })
            .WithMessage("The first block overlaps another existing block.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var block = await blocks.GetByIdAsync(command.OriginalBlockId, ct);
                if (block is null) return true; // reported above
                var overlapping = await blocks.GetOverlappingAsync(
                    command.SecondBlockStart, command.SecondBlockEnd, clock.UtcNow,
                    ritualOnly: block is FocusBlock, excludeId: command.OriginalBlockId, cancellationToken: ct);
                return overlapping.Count == 0;
            })
            .WithMessage("The second block overlaps another existing block.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var block = await blocks.GetByIdAsync(command.OriginalBlockId, ct);
                if (block is null) return true; // reported above
                var originalDuration = block.GetDuration();
                var splitDuration = (command.FirstBlockEnd - command.FirstBlockStart) + (command.SecondBlockEnd - command.SecondBlockStart);
                return splitDuration == originalDuration;
            })
            .WithMessage("The two resulting blocks must add up to the original block's total duration.");
    }
}
