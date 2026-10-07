using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.GetBlockPlacementVerdict;

/// <summary>
/// "May a block go here?" — the one placement question Create/Update/Split validators and the
/// Planner's drop all ask, so preview and save run the same code (Gap 88 phase 8).
/// ExistingBlockId names the block being moved/split (read for its membership, left out of the
/// calendar). A Cover Deficit tile asks the contract first, in its own validator (Gap 106).
/// </summary>
public sealed record GetBlockPlacementVerdictQuery(
    TimeSlot Slot,
    BlockPresetType Type,
    Guid PresetId,
    Guid? ExistingBlockId = null) : IQuery<BlockPlacementVerdictDto>;
