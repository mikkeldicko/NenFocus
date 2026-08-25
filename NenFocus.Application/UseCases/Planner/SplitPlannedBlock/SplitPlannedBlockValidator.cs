using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;

namespace NenFocus.Application.UseCases.Planner.SplitPlannedBlock;

public sealed class SplitPlannedBlockValidator : AbstractValidator<SplitPlannedBlockCommand>
{
    public SplitPlannedBlockValidator(
        IPlannedBlockRepository blocks,
        ISessionRepository sessions,
        INenContractRepository contracts,
        ISystemClock clock)
    {
        // The local calendar day(s) the original block spans — null when the block can't
        // be resolved (reported by the "block to split could not be found" rule instead).
        // A cross-midnight original spans two days; Split lets its two pieces land on
        // either one, rather than forcing everything onto the start day alone.
        async Task<HashSet<DateOnly>?> OriginalBlockDaysAsync(Guid blockId, CancellationToken ct)
        {
            var block = await blocks.GetByIdAsync(blockId, ct);
            if (block is null) return null;
            var localStart = clock.ToLocal(block.Start);
            var localEnd = clock.ToLocal(block.End);
            return DayRangeSplitter.Split(localStart, localEnd).Select(s => s.Date).ToHashSet();
        }

        // A resulting piece must land entirely on a single day (no recursive crossing —
        // Split's dialog has no date-rollover UI, so a piece spanning midnight itself has
        // nowhere to be entered), and that day must be one the original actually touched.
        bool ResolvesToSingleDayIn(DateTime start, DateTime end, HashSet<DateOnly> allowedDays)
        {
            var startDate = clock.ToLocalDate(start);
            // Exact-midnight end (e.g. ...-24:00) has no presence on the next day — see
            // UpdatePlannedBlockValidator's identical fix for the full rationale.
            var endDate = DateOnly.FromDateTime(clock.ToLocal(end).AddTicks(-1));
            return startDate == endDate && allowedDays.Contains(startDate);
        }

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

        // Split is Focus-only — a Background block has no session and no Daily Floor
        // relevance, so there's no real use case for reallocating its hours, and it has no
        // duration cap (unlike Focus, capped at 12h — see Part 2 of Gap K), so allowing it
        // here would reopen the N-day case the next rule deliberately guards against.
        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var block = await blocks.GetByIdAsync(command.OriginalBlockId, ct);
                return block is null || block is FocusBlock;
            })
            .WithMessage("Only a Focus block can be split.");

        // Defensive guard: Focus blocks aren't capped at 12h yet (that's Part 2 of Gap K,
        // not yet built), so an original spanning more than two days is still technically
        // constructible today. The day-membership logic below only reasons about "the
        // original's day-set" in the abstract and would silently misbehave on a 3+ day
        // span rather than failing loud — reject it outright instead.
        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var days = await OriginalBlockDaysAsync(command.OriginalBlockId, ct);
                return days is null || days.Count <= 2;
            })
            .WithMessage("A block spanning more than two calendar days cannot be split.");

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
                var days = await OriginalBlockDaysAsync(command.OriginalBlockId, ct);
                if (days is null) return true; // reported above
                return ResolvesToSingleDayIn(command.FirstBlockStart, command.FirstBlockEnd, days);
            })
            .WithMessage("The first block must land entirely on one of the days the original block spans.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var days = await OriginalBlockDaysAsync(command.OriginalBlockId, ct);
                if (days is null) return true; // reported above
                return ResolvesToSingleDayIn(command.SecondBlockStart, command.SecondBlockEnd, days);
            })
            .WithMessage("The second block must land entirely on one of the days the original block spans.");

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
