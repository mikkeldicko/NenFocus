using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.DeletePlannedBlock;

public sealed class DeletePlannedBlockCommandHandler : ICommandHandler<DeletePlannedBlockCommand, DeletePlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;

    public DeletePlannedBlockCommandHandler(IPlannedBlockRepository blocks)
        => _blocks = blocks;

    public async Task<DeletePlannedBlockResult> HandleAsync(DeletePlannedBlockCommand command, CancellationToken cancellationToken = default)
    {
        await _blocks.DeleteAsync(command.BlockId, cancellationToken);
        return DeletePlannedBlockResult.Ok();
    }
}
