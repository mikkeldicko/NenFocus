using FluentValidation;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.DeletePlannedBlock;

public sealed class DeletePlannedBlockValidator : AbstractValidator<DeletePlannedBlockCommand>
{
    public DeletePlannedBlockValidator(
        IPlannedBlockRepository blocks,
        ISessionRepository sessions,
        ISystemClock clock)
    {
        RuleFor(x => x.BlockId)
            .NotEmpty().WithMessage("Block ID is required.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.BlockId == Guid.Empty) return true;
                var block = await blocks.GetByIdAsync(command.BlockId, ct);
                return block is null || !block.HasEnded(clock.UtcNow);
            })
            .WithMessage("This block has already ended and is kept as a historical record — it cannot be deleted.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.BlockId == Guid.Empty) return true;
                var active = await sessions.GetActiveAsync(ct);
                return active?.OriginatingBlockId != command.BlockId;
            })
            .WithMessage("This block is the active session's originating block and cannot be deleted while the session is running.");
    }
}
