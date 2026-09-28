using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
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
        IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> computePlannedFocusHours)
    {
        _blocks = blocks;
        _clock = clock;

        RuleFor(x => x.BlockId)
            .NotEmpty().WithMessage("Block ID is required.");

        RuleFor(x => x.PresetId)
            .NotEmpty().WithMessage("Preset ID is required.");

        // Mirrors DeletePlannedBlockValidator's identical guard — an already-ended block
        // (Missed, EndedEarly, or otherwise resolved) is kept as a historical record and
        // must be just as immutable to Update as it already is to Delete.
        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.BlockId == Guid.Empty) return true;
                var block = await _blocks.GetByIdAsync(command.BlockId, ct);
                return block is null || !block.HasEnded(_clock.UtcNow);
            })
            .WithMessage("This block has already ended and is kept as a historical record — it cannot be edited.");

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
                return block is not FocusBlock || (command.End - command.Start) <= TimeSpan.FromHours(FocusBlock.MaxDurationHours);
            })
            .WithMessage($"A Focus block cannot exceed {FocusBlock.MaxDurationHours} hours.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var block = await _blocks.GetByIdAsync(command.BlockId, ct);
                if (block?.ContractId is not { } contractId) return true;
                var contract = await contracts.GetByIdAsync(contractId, ct);
                return contract is null
                    || contract.AllowsBlockPlacement(_clock.ToLocal(command.Start), _clock.ToLocal(command.End));
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

                var originalDate = _clock.ToLocalDate(block.Start);
                var newStartDate = _clock.ToLocalDate(command.Start);
                // A block ending exactly at midnight (e.g. 22:00-24:00) has no actual
                // presence on the next calendar day — resolve End's boundary to the day it
                // closes out, mirroring the [start, end) exclusive-end semantics
                // PlannedBlock.IsActive already uses everywhere else. Subtracting one tick
                // only changes the resolved date when End lands exactly on a day boundary.
                var newEndDate = DateOnly.FromDateTime(_clock.ToLocal(command.End).AddTicks(-1));

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
                                    DraftFocusBlockRanges: [(command.Start, command.End)],
                                    ExcludeBlockId: command.BlockId),
                                ct);
                            var newDayTotal = plannedByDay.GetValueOrDefault(originalDate);

                            if (!contract.IsDayMet(originalDate, newDayTotal))
                            {
                                var floorHours = contract.FloorHoursFor(originalDate);
                                context.AddFailure(nameof(UpdatePlannedBlockCommand.End),
                                    $"This would drop {originalDate:MMM d}'s total below its {floorHours:0.#}h daily floor.");
                            }
                        }
                        else if (contract.StrictnessRule == StrictnessRule.PeriodTimeBudget)
                        {
                            var plannedByDay = await computePlannedFocusHours.HandleAsync(
                                new ComputePlannedFocusHoursQuery(
                                    contractId,
                                    DraftFocusBlockRanges: [(command.Start, command.End)],
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
                                    context.AddFailure(nameof(UpdatePlannedBlockCommand.End),
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
                                        context.AddFailure(nameof(UpdatePlannedBlockCommand.End),
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
                context.AddFailure(nameof(UpdatePlannedBlockCommand.Start), message);
            });
    }
}
