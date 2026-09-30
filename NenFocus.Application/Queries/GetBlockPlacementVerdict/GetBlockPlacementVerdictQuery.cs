using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.GetBlockPlacementVerdict;

/// <summary>
/// "May a block go here?" — the one placement question Create/Update/Split validators and the
/// Planner's drop all ask, so preview and save run the same code (Gap 88 phase 8). ContractId
/// scopes a *new* block's redemption-attribution search; ExistingBlockId names the block being
/// moved/split (read for its contract, left out of the calendar) — never passed to the checker.
/// </summary>
public sealed record GetBlockPlacementVerdictQuery(
    TimeSlot Slot,
    BlockPresetType Type,
    Guid PresetId,
    Guid? ContractId = null,
    Guid? ExistingBlockId = null) : IQuery<BlockPlacementVerdictDto>;
