using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
namespace NenFocus.Application.DTOs;

/// <summary>
/// One block to create atomically alongside a Nen Contract mutation (Seal or Extend).
/// Mirrors the wizard's draft list — nothing here is persisted until the whole command
/// commits. Shared the same way SessionBreakDto is: a plain supporting shape, not part
/// of any single vertical slice's own four files.
/// </summary>
public sealed record NenContractBlockSpec(
    Guid PresetId,
    TimeSlot Slot,
    BlockPresetType BlockPresetType,
    string? Title = null);
