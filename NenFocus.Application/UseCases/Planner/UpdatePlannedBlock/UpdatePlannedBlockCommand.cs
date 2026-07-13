using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;

public sealed record UpdatePlannedBlockCommand : ICommand<UpdatePlannedBlockResult>
{
    public Guid BlockId { get; init; }
    public Guid PresetId { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }

    /// <summary>
    /// Focus blocks only — replaces the block's make-up coverage entirely. Deliberately
    /// no ContractId field here: re-parenting a block to a different contract isn't a
    /// designed operation for the ordinary edit form (only Seal/Extend set it, only
    /// Break's cleanup clears it) — adding a mutable field for it here would risk silently
    /// unlinking a block on an unrelated time/preset edit that forgot to pass it through.
    /// </summary>
    public IReadOnlyList<MakeUpForEntryDto> MakeUpForEntries { get; init; } = [];
}
