using FluentValidation;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Application.Queries.GetBlockPlacementVerdict;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
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
        IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> computePlannedFocusHours,
        IQueryHandler<GetBlockPlacementVerdictQuery, BlockPlacementVerdictDto> placement)
    {
        _blocks = blocks;
        _clock = clock;

        RuleFor(x => x.BlockId)
            .NotEmpty().WithMessage("Block ID is required.");

        RuleFor(x => x.PresetId)
            .NotEmpty().WithMessage("Preset ID is required.");

        // A started block is locked — no move, resize, end or preset change (Gap 88 phase 0).
        // It ends early only through End Early or by breaking its contract.
        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.BlockId == Guid.Empty) return true;
                var block = await _blocks.GetByIdAsync(command.BlockId, ct);
                return block is null || !block.HasStarted(_clock.Now);
            })
            .WithMessage("This block has already started — it cannot be edited.");

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

        // The one placement rule (Gap 88 phase 8): not in the past → inside the contract's
        // window → free for Focus, resolved against the capped window attribution returns.
        RuleFor(x => x)
            .CustomAsync(async (command, context, ct) =>
            {
                var block = await _blocks.GetByIdAsync(command.BlockId, ct);
                if (block is null) return;
                var verdict = await placement.HandleAsync(
                    new GetBlockPlacementVerdictQuery(command.Slot, block.PresetType, command.PresetId, ExistingBlockId: command.BlockId),
                    ct);
                if (verdict.Outcome != PlacementOutcome.Allowed && verdict.Message is not null)
                    context.AddFailure(nameof(UpdatePlannedBlockCommand.Slot), verdict.Message);
            });

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
                if (block?.Membership?.ContractId is not { } contractId) return;

                var contract = await contracts.GetByIdAsync(contractId, ct);
                if (contract is null || !contract.IsInEffect) return;

                if (block.BlockPresetId != command.PresetId)
                {
                    context.AddFailure(nameof(UpdatePlannedBlockCommand.PresetId),
                        "This block's preset cannot be changed while it belongs to a sealed contract.");
                    return;
                }

                // The local days each slot touches, half-open — a block ending exactly at
                // midnight (e.g. 22:00-24:00) has no presence on the next day (Gap 88).
                var originalDate = _clock.TimeZone.SplitByDay(block.Scheduled).First().Date;
                var newDays = _clock.TimeZone.SplitByDay(command.Slot).Select(p => p.Date).ToList();
                var newStartDate = newDays.First();
                var newEndDate = newDays.Last();

                if (contract.AllowsBlockTimeChange(originalDate, newStartDate, newEndDate))
                {
                    if (block is FocusBlock)
                    {
                        // Floor-protection: a Daily Flexibility contract lets you shrink a
                        // Focus block freely as long as the day's total doesn't drop below
                        // its own floor. A pure same-duration move never changes the day's
                        // total, so this only ever bites an actual shrink. Reuses IsDayMet
                        // (the same domain rule Day-Met scoring already uses) rather than
                        // re-deriving the comparison here.
                        if (contract.StrictnessRule == StrictnessRule.DailyFlexibility)
                        {
                            var plannedByDay = await computePlannedFocusHours.HandleAsync(
                                new ComputePlannedFocusHoursQuery(
                                    contractId,
                                    DraftFocusBlockRanges: [command.Slot],
                                    ExcludeBlockId: command.BlockId),
                                ct);
                            var newDayTotal = plannedByDay.GetValueOrDefault(originalDate);

                            if (!contract.IsDayMet(originalDate, newDayTotal))
                            {
                                var floorHours = contract.FloorHoursFor(originalDate);
                                context.AddFailure(nameof(UpdatePlannedBlockCommand.Slot),
                                    $"This would drop {originalDate:MMM d}'s total below its {floorHours:0.#}h daily floor.");
                            }
                        }
                        else if (contract.StrictnessRule == StrictnessRule.PeriodTimeBudget)
                        {
                            var plannedByDay = await computePlannedFocusHours.HandleAsync(
                                new ComputePlannedFocusHoursQuery(
                                    contractId,
                                    DraftFocusBlockRanges: [command.Slot],
                                    ExcludeBlockId: command.BlockId),
                                ct);

                            // Period-total-protection: a Period Time Budget contract using a
                            // whole-period ContractFloor instead of a per-day floor. Only
                            // ever fires when ContractFloor is actually set (IsPeriodMet is
                            // trivially true otherwise).
                            if (contract.ContractFloor is not null)
                            {
                                var newPeriodTotal = plannedByDay.Values.Sum();
                                if (!contract.IsPeriodMet(newPeriodTotal))
                                {
                                    context.AddFailure(nameof(UpdatePlannedBlockCommand.Slot),
                                        $"This would drop the contract's total planned hours below its {contract.ContractFloorHours:0.#}h target.");
                                }
                            }
                            // Ordinary per-day floor: unlike Daily Flexibility, a Period move
                            // can land on a different day entirely — both the day being
                            // vacated and the day being landed on (if different) need their
                            // own floor re-checked, since plannedByDay already reflects this
                            // block's move out of one and into the other.
                            else
                            {
                                foreach (var date in new[] { originalDate, newStartDate }.Distinct())
                                {
                                    var total = plannedByDay.GetValueOrDefault(date);
                                    if (!contract.IsDayMet(date, total))
                                    {
                                        var floorHours = contract.FloorHoursFor(date);
                                        context.AddFailure(nameof(UpdatePlannedBlockCommand.Slot),
                                            $"This would drop {date:MMM d}'s total below its {floorHours:0.#}h daily floor.");
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    return;
                }

                var message = contract.StrictnessRule switch
                {
                    StrictnessRule.Locked => "This block is locked by a Lockdown contract.",
                    StrictnessRule.DailyFlexibility => "Daily Flex contracts only allow moving blocks within the same day.",
                    _ => "Cannot move block outside the contract period." // PeriodTimeBudget
                };
                context.AddFailure(nameof(UpdatePlannedBlockCommand.Slot), message);
            });
    }
}
