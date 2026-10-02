using FluentValidation;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetBlockPlacementVerdict;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed class CreatePlannedBlockValidator : AbstractValidator<CreatePlannedBlockCommand>
{
    public CreatePlannedBlockValidator(
        IBlockPresetRepository presets,
        IQueryHandler<GetBlockPlacementVerdictQuery, BlockPlacementVerdictDto> placement)
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

        // The slot is required (the compiler enforces it) and guarantees end after start
        // (Gap 88) — so there's no "missing" or "end before start" rule here. "Not in the
        // past" and "inside the contract's hours" are the placement query's job below.
        RuleFor(x => x.Slot)
            .Must(slot => slot.Length >= TimeSpan.FromMinutes(1))
            .WithMessage("Block must be at least 1 minute long.")
            // Mirrors FocusBlock.Create's own cap — the entity is the real, unbypassable
            // invariant; this exists only to produce a friendly FormError instead of an
            // unhandled exception.
            .Must((command, slot) => command.BlockPresetType != BlockPresetType.Focus
                || slot.Length <= TimeSpan.FromHours(FocusBlock.MaxDurationHours))
            .WithMessage($"A Focus block cannot exceed {FocusBlock.MaxDurationHours} hours.");

        // The one placement rule (Gap 88 phase 8): not in the past → inside the contract's
        // window → free for Focus. The query finds the contract the slot sits in and owns the
        // wording, so preview and save agree.
        RuleFor(x => x)
            .CustomAsync(async (command, context, ct) =>
            {
                var verdict = await placement.HandleAsync(
                    new GetBlockPlacementVerdictQuery(command.Slot, command.BlockPresetType, command.PresetId, command.DeclineRedemption),
                    ct);
                if (verdict.Outcome != PlacementOutcome.Allowed && verdict.Message is not null)
                    context.AddFailure(nameof(CreatePlannedBlockCommand.Slot), verdict.Message);
            });
    }
}
