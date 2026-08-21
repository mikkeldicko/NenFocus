using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed record CreatePlannedBlockCommand : ICommand<CreatePlannedBlockResult>
{
    public Guid PresetId { get; init; }
    public BlockPresetType BlockPresetType { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }

    /// <summary>Set when this block is created directly under an already-active contract
    /// (e.g. a redemption block via the "Redemption" form section), outside Seal/Extend's
    /// own atomic batch.</summary>
    public Guid? ContractId { get; init; }

    /// <summary>Focus blocks only — see BlockPresetType. Empty means an ordinary block.</summary>
    public IReadOnlyList<RedemptionEntryDto> RedemptionEntries { get; init; } = [];
}
