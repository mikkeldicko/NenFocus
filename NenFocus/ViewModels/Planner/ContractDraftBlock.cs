using System;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.ViewModels.Planner;

/// <summary>
/// A block dragged out onto the Planner calendar while drafting a Nen Contract.
/// Held only in memory until Seal — Id is client-only and never persisted.
/// Converted into a NenContractBlockSpec when the contract is sealed.
/// </summary>
public sealed record ContractDraftBlock(
    Guid Id,
    Guid PresetId,
    string PresetName,
    TimeSlot Slot,
    bool IsFocus);
