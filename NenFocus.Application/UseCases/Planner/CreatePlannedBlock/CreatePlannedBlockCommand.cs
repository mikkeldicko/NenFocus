using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed record CreatePlannedBlockCommand : ICommand<CreatePlannedBlockResult>
{
    public Guid PresetId { get; init; }
    public BlockPresetType BlockPresetType { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }

    /// <summary>
    /// Set when this block is created directly under an already-known contract (e.g. the
    /// type-changed delete-then-recreate path preserving the original block's contract),
    /// outside Seal/Extend's own atomic batch. Null for an ordinary new Focus block — the
    /// handler auto-discovers whether it should attach to a contract and cover a deficit
    /// via ComputeRedemptionAttributionQuery; there is no client-driven selection.
    /// </summary>
    public Guid? ContractId { get; init; }
}
