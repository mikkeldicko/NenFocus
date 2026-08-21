using FluentValidation;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.DeletePlannedBlock;

public sealed class DeletePlannedBlockValidator : AbstractValidator<DeletePlannedBlockCommand>
{
    public DeletePlannedBlockValidator(
        IPlannedBlockRepository blocks,
        ISessionRepository sessions)
    {
        RuleFor(x => x.BlockId)
            .NotEmpty().WithMessage("Block ID is required.");

        // A block once linked to a Nen Contract is permanently protected from deletion —
        // even after the contract concludes — since it feeds that contract's historical
        // Day-Met/completed-hours scoring. A block never linked to a contract is free to
        // delete regardless of whether it has ended: an ended, non-contract block's only
        // remaining role is display in the Planner snapshot view, which is exactly what
        // this rule now permits removing.
        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.BlockId == Guid.Empty) return true;
                var block = await blocks.GetByIdAsync(command.BlockId, ct);
                return block?.ContractId is null;
            })
            .WithMessage("Cannot delete a block that belongs to a Nen Contract.");

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
