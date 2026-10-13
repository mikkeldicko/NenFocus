using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Interfaces;

/// <summary>
/// Repository contract for BlockPreset aggregate persistence.
/// Implemented by NenFocus.Infrastructure.
/// </summary>
public interface IBlockPresetRepository
{
    /// <summary>
    /// Returns all stored block presets.
    /// </summary>
    Task<IReadOnlyList<BlockPreset>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single block preset by Id, or null if not found.
    /// </summary>
    Task<BlockPreset?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if a preset with the given name already exists for the given
    /// PresetType. Scoped by type, not global — a Focus preset and a Background preset
    /// are chosen from separate, type-filtered contexts and never compared against each
    /// other, so sharing a name isn't a real collision (mirrors the (ResourceName,
    /// ResourceType) composite-key idiom already used elsewhere in this codebase).
    /// </summary>
    Task<bool> ExistsWithNameAsync(
        string name,
        BlockPresetType presetType,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new block preset; it is saved when the command commits (Gap 110).</summary>
    void Add(BlockPreset preset);

    /// <summary>Removes a loaded block preset; saved when the command commits. Ask first whether it may go.</summary>
    void Remove(BlockPreset preset);
}
