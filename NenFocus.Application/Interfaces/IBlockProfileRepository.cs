using NenFocus.Domain.Entities;

namespace NenFocus.Application.Interfaces;

/// <summary>
/// Repository contract for BlockProfile aggregate persistence.
/// Implemented by NenFocus.Infrastructure.
/// </summary>
public interface IBlockProfileRepository
{
    /// <summary>
    /// Returns all stored block profiles.
    /// </summary>
    Task<IReadOnlyList<BlockProfile>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single block profile by Id, or null if not found.
    /// </summary>
    Task<BlockProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all profiles whose tags include any of the provided tags.
    /// </summary>
    Task<IReadOnlyList<BlockProfile>> GetByTagsAsync(
        IEnumerable<string> tags,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a new block profile.
    /// </summary>
    Task AddAsync(BlockProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists changes to an existing block profile.
    /// </summary>
    Task UpdateAsync(BlockProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a block profile by Id.
    /// Throws if the profile is referenced by a scheduled planned block.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if a profile with the given name already exists.
    /// Used by validators to enforce unique profile names.
    /// </summary>
    Task<bool> ExistsWithNameAsync(
        string name,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);
}
