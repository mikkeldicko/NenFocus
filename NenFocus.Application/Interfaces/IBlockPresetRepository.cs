using NenFocus.Domain.Entities;

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
    /// Returns all presets whose tags include any of the provided tags.
    /// </summary>
    Task<IReadOnlyList<BlockPreset>> GetByTagsAsync(
        IEnumerable<string> tags,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a new block preset.
    /// </summary>
    Task AddAsync(BlockPreset preset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists changes to an existing block preset.
    /// </summary>
    Task UpdateAsync(BlockPreset preset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a block preset by Id.
    /// Throws if the preset is referenced by a scheduled planned block.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if a preset with the given name already exists.
    /// Used by validators to enforce unique preset names.
    /// </summary>
    Task<bool> ExistsWithNameAsync(
        string name,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);
}
