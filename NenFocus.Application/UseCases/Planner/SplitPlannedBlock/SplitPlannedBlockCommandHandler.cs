using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.UseCases.Planner.SplitPlannedBlock;

public sealed class SplitPlannedBlockCommandHandler : ICommandHandler<SplitPlannedBlockCommand, SplitPlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;

    public SplitPlannedBlockCommandHandler(IPlannedBlockRepository blocks)
    {
        _blocks = blocks;
    }

    public async Task<SplitPlannedBlockResult> HandleAsync(SplitPlannedBlockCommand command, CancellationToken cancellationToken = default)
    {
        // Only a Focus block a contract binds can be split; the validator refuses anything else
        // (a Background block: SplitNotOffered).
        if (await _blocks.GetByIdAsync(command.OriginalBlockId, cancellationToken) is not FocusBlock original)
            return SplitPlannedBlockResult.Fail($"Planned block '{command.OriginalBlockId}' not found.");

        // The block divides itself: both pieces keep its membership, and its receipt is divided in
        // time order — never recalculated, no piece shortened (Gap 106).
        var (first, second) = original.SplitInto(Guid.NewGuid(), command.First, Guid.NewGuid(), command.Second);

        await _blocks.DeleteAsync(original.Id, cancellationToken);
        await _blocks.AddAsync(first, cancellationToken);
        await _blocks.AddAsync(second, cancellationToken);

        return SplitPlannedBlockResult.Ok(first.Id, second.Id);
    }
}
