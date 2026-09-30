using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
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
        ISystemClock clock,
        IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> computePlannedFocusHours)
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
        // duration cap (unlike Focus, capped at FocusBlock.MaxDurationHours), so allowing
        // it here would reopen the N-day case OriginalBlockDaysAsync's 2-day assumption
        // relies on. No separate N-day guard is needed: FocusBlock.Create/Reschedule
        // themselves make a 3+ day Focus block unconstructible, so this rule alone is
        // sufficient to keep the day-membership logic below within its 2-day assumption.
        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var block = await blocks.GetByIdAsync(command.OriginalBlockId, ct);
                return block is null || block is FocusBlock;
            })
            .WithMessage("Only a Focus block can be split.");

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
                return contract is not null && contract.StrictnessRule != StrictnessRule.Locked;
            })
            .WithMessage("Lockdown contracts are immutable — only Daily Flexibility and Period Time Budget contracts allow splitting a block.");

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
                return block is not null && !block.HasStarted(clock.UtcNow);
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

        // Surfaced 2026-09-02: Split never checked Work Hours at all, unlike
        // CreatePlannedBlockValidator/UpdatePlannedBlockValidator, which both call
        // AllowsBlockPlacement — a real pre-existing gap, not new scope creep. Split is
        // Focus-only (enforced above) and always belongs to a sealed contract (also
        // enforced above), so this applies unconditionally to both resulting pieces,
        // unlike Create's own version of this rule which only applies when
        // BlockPresetType == Focus.
        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var block = await blocks.GetByIdAsync(command.OriginalBlockId, ct);
                if (block?.ContractId is not { } contractId) return true; // reported above
                var contract = await contracts.GetByIdAsync(contractId, ct);
                return contract is null || contract.AllowsBlockPlacement(clock.ToLocal(command.FirstBlockStart), clock.ToLocal(command.FirstBlockEnd));
            })
            .WithMessage("The first block falls outside the contract's configured Work Hours for that day.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var block = await blocks.GetByIdAsync(command.OriginalBlockId, ct);
                if (block?.ContractId is not { } contractId) return true; // reported above
                var contract = await contracts.GetByIdAsync(contractId, ct);
                return contract is null || contract.AllowsBlockPlacement(clock.ToLocal(command.SecondBlockStart), clock.ToLocal(command.SecondBlockEnd));
            })
            .WithMessage("The second block falls outside the contract's configured Work Hours for that day.");

        // Floor-protection: unlike an ordinary same-preset shrink (which conserves the
        // day's total by construction), Split can freely reallocate hours across whichever
        // day(s) the original spans — including concentrating everything on one day and
        // leaving the other short. Reuses IsDayMet/FloorHoursFor/IsPeriodMet (the same
        // domain rules UpdatePlannedBlockValidator's own floor checks already use) rather
        // than re-deriving the comparison here. Branches on StrictnessRule exactly like
        // Update's own edit guard: Daily Flexibility and a Period Time Budget contract with
        // no whole-period ContractFloor both fall back to the same per-day check (the days
        // the original vacates, unioned with the days the two new pieces land on); a Period
        // Time Budget contract that does carry a ContractFloor checks the whole period's
        // planned total instead, since there's no per-day obligation to protect.
        RuleFor(x => x)
            .CustomAsync(async (command, context, ct) =>
            {
                var block = await blocks.GetByIdAsync(command.OriginalBlockId, ct);
                if (block?.ContractId is not { } contractId) return; // reported above

                var contract = await contracts.GetByIdAsync(contractId, ct);
                if (contract is null || contract.StrictnessRule == StrictnessRule.Locked) return; // reported above

                var plannedByDay = await computePlannedFocusHours.HandleAsync(
                    new ComputePlannedFocusHoursQuery(
                        contractId,
                        DraftFocusBlockRanges:
                        [
                            (command.FirstBlockStart, command.FirstBlockEnd),
                            (command.SecondBlockStart, command.SecondBlockEnd)
                        ],
                        ExcludeBlockId: command.OriginalBlockId),
                    ct);

                if (contract.StrictnessRule == StrictnessRule.PeriodTimeBudget && contract.ContractFloor is not null)
                {
                    var newPeriodTotal = plannedByDay.Values.Sum();
                    if (!contract.IsPeriodMet(newPeriodTotal))
                    {
                        context.AddFailure(nameof(SplitPlannedBlockCommand.FirstBlockEnd),
                            $"This would drop the contract's total planned hours below its {contract.ContractFloorHours:0.#}h target.");
                    }
                    return;
                }

                // The days the original is being vacated from union the days the two new
                // pieces land on — a day the original touched but neither piece lands on
                // anymore is exactly the case this check exists for (abandoned entirely,
                // its total can drop without either piece being there to trigger a check).
                var originalDays = await OriginalBlockDaysAsync(command.OriginalBlockId, ct) ?? [];
                var touchedDays = originalDays
                    .Append(clock.ToLocalDate(command.FirstBlockStart))
                    .Append(clock.ToLocalDate(command.SecondBlockStart))
                    .Distinct();

                foreach (var date in touchedDays)
                {
                    var total = plannedByDay.GetValueOrDefault(date);
                    if (!contract.IsDayMet(date, total))
                    {
                        var floorHours = contract.FloorHoursFor(date);
                        context.AddFailure(nameof(SplitPlannedBlockCommand.FirstBlockEnd),
                            $"This would drop {date:MMM d}'s total below its {floorHours:0.#}h daily floor.");
                        break;
                    }
                }
            });
    }
}
