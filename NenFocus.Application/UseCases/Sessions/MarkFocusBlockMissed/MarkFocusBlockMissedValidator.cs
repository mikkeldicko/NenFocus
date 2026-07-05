using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.UseCases.Sessions.MarkFocusBlockMissed;

public sealed class MarkFocusBlockMissedValidator : AbstractValidator<MarkFocusBlockMissedCommand>
{
    public MarkFocusBlockMissedValidator(IPlannedBlockRepository blocks, ISessionRepository sessions)
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
                return block is null || block.RitualCompletedAt is null;
            })
            .WithMessage("The ritual for this block was already completed — it cannot be marked missed.");

        RuleFor(x => x.FocusBlockId)
            .MustAsync(async (id, ct) =>
                id == Guid.Empty || !await sessions.ExistsForOriginatingBlockAsync(id, ct))
            .WithMessage("A session already exists for this focus block.");
    }
}
