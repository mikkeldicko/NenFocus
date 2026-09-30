using FluentValidation;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.DeletePlannedBlock;

public sealed class DeletePlannedBlockValidator : AbstractValidator<DeletePlannedBlockCommand>
{
    public DeletePlannedBlockValidator(
        IPlannedBlockRepository blocks,
        ISystemClock clock)
    {
        RuleFor(x => x.BlockId)
            .NotEmpty().WithMessage("Block ID is required.");

        // A block once linked to a Nen Contract is permanently protected from deletion —
        // even after the contract concludes — since it feeds that contract's historical
        // Day-Met/completed-hours scoring. A block never linked to a contract may be deleted
        // before it starts or after it has ended (an ended one's only remaining role is display
        // in the Planner snapshot view) — never while it runs (rule below).
        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.BlockId == Guid.Empty) return true;
                var block = await blocks.GetByIdAsync(command.BlockId, ct);
                return block?.ContractId is null;
            })
            .WithMessage("Cannot delete a block that belongs to a Nen Contract.");

        // A started block runs to its end (Gap 88 phase 0). This also covers the active
        // session's originating block — a planned session only runs inside its block's window.
        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.BlockId == Guid.Empty) return true;
                var block = await blocks.GetByIdAsync(command.BlockId, ct);
                var now = clock.Now;
                return block is null || !block.HasStarted(now) || block.HasEnded(now);
            })
            .WithMessage("This block is running and cannot be deleted until it ends.");
    }
}
