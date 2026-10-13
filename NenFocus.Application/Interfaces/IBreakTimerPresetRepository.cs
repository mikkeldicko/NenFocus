using NenFocus.Domain.Entities;

namespace NenFocus.Application.Interfaces;

/// <summary>
/// CRUD persistence contract for BreakTimerPreset — mirrors IContractPresetRepository's
/// shape exactly. No lock-check method (HasFutureBlocksForPresetAsync's equivalent):
/// unlike BlockPreset/ContractPreset, a BreakTimerPreset is snapshotted by value into
/// a Session at Start, so nothing ever needs to ask "is this preset currently in use"
/// before allowing a delete.
/// </summary>
public interface IBreakTimerPresetRepository
{
    Task<IReadOnlyList<BreakTimerPreset>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<BreakTimerPreset?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if a preset with the given name already exists. Used by validators
    /// to enforce unique preset names; excludeId omits the preset being updated from
    /// its own uniqueness check. Mirrors IContractPresetRepository's global (unscoped)
    /// uniqueness check — no PresetType-style split like BlockPreset's.
    /// </summary>
    Task<bool> ExistsWithNameAsync(
        string name,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new break timer preset; it is saved when the command commits (Gap 110).</summary>
    void Add(BreakTimerPreset preset);

    /// <summary>Removes a loaded break timer preset; saved when the command commits. Ask first whether it may go.</summary>
    void Remove(BreakTimerPreset preset);
}
