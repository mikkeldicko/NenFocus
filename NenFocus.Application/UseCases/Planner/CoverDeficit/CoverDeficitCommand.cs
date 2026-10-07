using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Planner.CoverDeficit;

/// <summary>
/// Place one redemption block covering <see cref="PresetId"/>'s deficit in the contract the Cover
/// Deficit button belongs to — the only path that creates a redemption block (Gap 106). The client
/// names which debt the tile pays; the contract decides whether the slot may pay it and what it covers.
/// </summary>
public sealed record CoverDeficitCommand : ICommand<CoverDeficitResult>
{
    public Guid ContractId { get; init; }
    public Guid PresetId { get; init; }
    public required TimeSlot Slot { get; init; }
}
