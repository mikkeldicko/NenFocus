using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;

public sealed class UpdatePlannedBlockValidator : AbstractValidator<UpdatePlannedBlockCommand>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISystemClock _clock;

    public UpdatePlannedBlockValidator(
        IPlannedBlockRepository blocks,
        IBlockProfileRepository profiles,
        ISystemClock clock)
    {
        _blocks = blocks;
        _clock = clock;

        RuleFor(x => x.BlockId)
            .NotEmpty().WithMessage("Block ID is required.");

        RuleFor(x => x.ProfileId)
            .NotEmpty().WithMessage("Profile ID is required.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.ProfileId == Guid.Empty || command.BlockId == Guid.Empty) return true;
                var block   = await _blocks.GetByIdAsync(command.BlockId, ct);
                var profile = await profiles.GetByIdAsync(command.ProfileId, ct);
                if (block is null || profile is null) return true;
                var required = block is FocusBlock ? BlockType.Focus : BlockType.Background;
                return profile.ProfileType == required;
            })
            .WithMessage("The selected profile type does not match this block type.");

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
    }
}
