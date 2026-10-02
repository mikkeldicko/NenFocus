using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed record CreatePlannedBlockCommand : ICommand<CreatePlannedBlockResult>
{
    public Guid PresetId { get; init; }
    public BlockPresetType BlockPresetType { get; init; }
    public required TimeSlot Slot { get; init; }

    /// <summary>
    /// True when the block must not cover a deficit — every manual creation; only the Cover
    /// Deficit flow leaves it false. The handler then skips ComputeRedemptionAttributionQuery:
    /// inside a contract in effect the block is a Bonus block, elsewhere a plain one. Which
    /// contract that is comes from where the slot sits, never from the client (Gap 97).
    /// </summary>
    public bool DeclineRedemption { get; init; }
}
