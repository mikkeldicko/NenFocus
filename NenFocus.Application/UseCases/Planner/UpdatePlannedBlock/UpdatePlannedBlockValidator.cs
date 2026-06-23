using FluentValidation;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;

public sealed class UpdatePlannedBlockValidator : AbstractValidator<UpdatePlannedBlockCommand>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISystemClock _clock;

    public UpdatePlannedBlockValidator(IPlannedBlockRepository blocks, ISystemClock clock)
    {
        _blocks = blocks;
        _clock = clock;

        RuleFor(x => x.BlockId)
            .NotEmpty().WithMessage("Block ID is required.");

        RuleFor(x => x.ProfileId)
            .NotEmpty().WithMessage("Profile ID is required.");

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
