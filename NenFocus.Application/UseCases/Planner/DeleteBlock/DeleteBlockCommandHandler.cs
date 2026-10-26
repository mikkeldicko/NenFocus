using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.DeleteBlock;

public sealed class DeleteBlockCommandHandler : ICommandHandler<DeleteBlockCommand, DeleteBlockResult>
{
    private readonly IBlockRepository _blocks;

    public DeleteBlockCommandHandler(IBlockRepository blocks)
        => _blocks = blocks;

    public async Task<DeleteBlockResult> HandleAsync(DeleteBlockCommand command, CancellationToken cancellationToken = default)
    {
        // Already gone is done: removing is idempotent (Gap 110).
        if (await _blocks.GetByIdAsync(command.BlockId, cancellationToken) is { } block)
            _blocks.Remove(block);
        return DeleteBlockResult.Ok();
    }
}
