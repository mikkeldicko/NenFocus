using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Interfaces;

/// <summary>
/// Repository contract for PlannedBlock persistence.
/// Implemented by NenFocus.Infrastructure.
/// </summary>
public interface IPlannedBlockRepository
{
    /// <summary>
    /// Returns all planned blocks whose time window intersects the given date.
    /// </summary>
    Task<IReadOnlyList<PlannedBlock>> GetForDayAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all planned blocks within a date range.
    /// Used by the Planner calendar view.
    /// </summary>
    Task<IReadOnlyList<PlannedBlock>> GetForRangeAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns any planned blocks that overlap with the given time window.
    /// Used by CreatePlannedBlockCommandValidator to enforce Focus+Focus overlap rule.
    /// Pass blockType to check only for Focus-on-Focus conflicts.
    /// </summary>
    Task<IReadOnlyList<PlannedBlock>> GetOverlappingAsync(
        DateTime start,
        DateTime end,
        BlockType? blockType = null,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single planned block by Id, or null if not found.
    /// </summary>
    Task<PlannedBlock?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a new planned block.
    /// </summary>
    Task AddAsync(PlannedBlock block, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists changes to an existing planned block.
    /// </summary>
    Task UpdateAsync(PlannedBlock block, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a planned block by Id.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
