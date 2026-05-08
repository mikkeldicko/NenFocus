using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.DeletePlannedBlock;

// ── Command ────────────────────────────────────────────────────────────────

public sealed record DeletePlannedBlockCommand
{
    public Guid BlockId { get; init; }
}

// ── Handler ────────────────────────────────────────────────────────────────

public sealed class DeletePlannedBlockCommandHandler
    : ICommandHandler<DeletePlannedBlockCommand>
{
    private readonly IPlannedBlockRepository _blocks;

    public DeletePlannedBlockCommandHandler(IPlannedBlockRepository blocks)
        => _blocks = blocks;

    public async Task<bool> HandleAsync(
        DeletePlannedBlockCommand command,
        CancellationToken cancellationToken = default)
    {
        await _blocks.DeleteAsync(command.BlockId, cancellationToken);
        return true;
    }
}
