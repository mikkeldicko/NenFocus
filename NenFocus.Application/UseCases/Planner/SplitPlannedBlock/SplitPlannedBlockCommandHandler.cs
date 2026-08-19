using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.UseCases.Planner.SplitPlannedBlock;

public sealed class SplitPlannedBlockCommandHandler : ICommandHandler<SplitPlannedBlockCommand, SplitPlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;

    public SplitPlannedBlockCommandHandler(IPlannedBlockRepository blocks)
        => _blocks = blocks;

    public async Task<SplitPlannedBlockResult> HandleAsync(SplitPlannedBlockCommand command, CancellationToken cancellationToken = default)
    {
        var original = await _blocks.GetByIdAsync(command.OriginalBlockId, cancellationToken);
        if (original is null)
            return SplitPlannedBlockResult.Fail($"Planned block '{command.OriginalBlockId}' not found.");

        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        PlannedBlock first;
        PlannedBlock second;

        if (original is FocusBlock focusOriginal)
        {
            first = FocusBlock.Create(
                firstId, focusOriginal.BlockPresetId, command.FirstBlockStart, command.FirstBlockEnd,
                focusOriginal.Title, focusOriginal.ContractId, focusOriginal.MakeUpForEntries);
            second = FocusBlock.Create(
                secondId, focusOriginal.BlockPresetId, command.SecondBlockStart, command.SecondBlockEnd,
                focusOriginal.Title, focusOriginal.ContractId, focusOriginal.MakeUpForEntries);
        }
        else
        {
            first = BackgroundBlock.Create(
                firstId, original.BlockPresetId, command.FirstBlockStart, command.FirstBlockEnd,
                original.Title, original.ContractId);
            second = BackgroundBlock.Create(
                secondId, original.BlockPresetId, command.SecondBlockStart, command.SecondBlockEnd,
                original.Title, original.ContractId);
        }

        await _blocks.DeleteAsync(original.Id, cancellationToken);
        await _blocks.AddAsync(first, cancellationToken);
        await _blocks.AddAsync(second, cancellationToken);

        return SplitPlannedBlockResult.Ok(firstId, secondId);
    }
}
