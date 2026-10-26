using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.UseCases.Planner.SplitBlock;

public sealed class SplitBlockCommandHandler : ICommandHandler<SplitBlockCommand, SplitBlockResult>
{
    private readonly IBlockRepository _blocks;

    public SplitBlockCommandHandler(IBlockRepository blocks)
    {
        _blocks = blocks;
    }

    public async Task<SplitBlockResult> HandleAsync(SplitBlockCommand command, CancellationToken cancellationToken = default)
    {
        // Only a Focus block a contract binds can be split; the validator refuses anything else
        // (a Background block: SplitNotOffered).
        if (await _blocks.GetByIdAsync(command.OriginalBlockId, cancellationToken) is not FocusBlock original)
            return SplitBlockResult.Fail($"Planned block '{command.OriginalBlockId}' not found.");

        // The block divides itself: both pieces keep its membership, and its receipt is divided in
        // time order — never recalculated, no piece shortened (Gap 106).
        var (first, second) = original.SplitInto(Guid.NewGuid(), command.First, Guid.NewGuid(), command.Second);

        _blocks.Remove(original);
        _blocks.Add(first);
        _blocks.Add(second);

        return SplitBlockResult.Ok(first.Id, second.Id);
    }
}
