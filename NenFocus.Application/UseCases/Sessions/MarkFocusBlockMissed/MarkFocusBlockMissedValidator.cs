using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.UseCases.Sessions.MarkFocusBlockMissed;

public sealed class MarkFocusBlockMissedValidator : AbstractValidator<MarkFocusBlockMissedCommand>
{
    public MarkFocusBlockMissedValidator(IPlannedBlockRepository blocks, ISessionRepository sessions, ISystemClock clock)
    {
        RuleFor(x => x.FocusBlockId)
            .NotEmpty().WithMessage("Focus block ID is required.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.FocusBlockId == Guid.Empty) return true;
                var block = await blocks.GetByIdAsync(command.FocusBlockId, ct);
                return block is FocusBlock;
            })
            .WithMessage("Focus block not found.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.FocusBlockId == Guid.Empty) return true;
                var block = await blocks.GetByIdAsync(command.FocusBlockId, ct) as FocusBlock;
                // The missed sweep's own rule (FocusBlock.IsUnresolved, Gap 95): window closed, Ritual
                // never done, not cancelled.
                return block is null || block.IsUnresolved(clock.Now);
            })
            .WithMessage("This block can't be marked missed — its window hasn't closed, its Ritual was done, or it was cancelled.");

        RuleFor(x => x.FocusBlockId)
            .MustAsync(async (id, ct) =>
                id == Guid.Empty || await sessions.GetByOriginatingBlockIdAsync(id, ct) is null)
            .WithMessage("A session already exists for this focus block.");
    }
}
