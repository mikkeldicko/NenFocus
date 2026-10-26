using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.DeletePlannedBlock;

public sealed class DeletePlannedBlockCommandHandler : ICommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;

    public DeletePlannedBlockCommandHandler(IPlannedBlockRepository blocks)
        => _blocks = blocks;

    public async Task<DeletePlannedBlockResult> HandleAsync(DeletePlannedBlockCommand command, CancellationToken cancellationToken = default)
    {
        // Already gone is done: removing is idempotent (Gap 110).
        if (await _blocks.GetByIdAsync(command.BlockId, cancellationToken) is { } block)
            _blocks.Remove(block);
        return DeletePlannedBlockResult.Ok();
    }
}
