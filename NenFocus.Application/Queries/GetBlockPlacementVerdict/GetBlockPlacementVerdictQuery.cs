using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.GetBlockPlacementVerdict;

/// <summary>
/// "May a block go here?" — the one placement question Create/Update/Split validators and the
/// Planner's drop all ask, so preview and save run the same code (Gap 88 phase 8).
/// DeclineRedemption mirrors the Create command's: a *new* Focus block that declines covers no
/// deficit, so its slot is checked as requested, never capped. ExistingBlockId names the block
/// being moved/split (read for its membership, left out of the calendar).
/// </summary>
public sealed record GetBlockPlacementVerdictQuery(
    TimeSlot Slot,
    BlockPresetType Type,
    Guid PresetId,
    bool DeclineRedemption = false,
    Guid? ExistingBlockId = null) : IQuery<BlockPlacementVerdictDto>;
