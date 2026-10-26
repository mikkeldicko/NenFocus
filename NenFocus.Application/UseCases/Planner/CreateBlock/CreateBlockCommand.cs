using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Planner.CreateBlock;

/// <summary>
/// A new plain block, or — inside a contract in effect — a Bonus block of it. Which contract that
/// is comes from where the slot sits, never from the client (Gap 97). A redemption block is made
/// only by <c>CoverDeficitCommand</c> (Gap 106).
/// </summary>
public sealed record CreateBlockCommand : ICommand<CreateBlockResult>
{
    public Guid PresetId { get; init; }
    public BlockPresetType BlockPresetType { get; init; }
    public required TimeSlot Slot { get; init; }
}
