using NenFocus.Domain.Entities;

namespace NenFocus.Application.Interfaces;

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
    /// </summary>
    Task<IReadOnlyList<PlannedBlock>> GetForRangeAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns any planned blocks that overlap with the given time window.
    /// If ritualOnly is true, only FocusBlocks are checked — used to
    /// enforce the Focus+Focus overlap rule at planning time.
    /// </summary>
    Task<IReadOnlyList<PlannedBlock>> GetOverlappingAsync(
        DateTime start,
        DateTime end,
        bool ritualOnly = false,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single planned block by Id, or null if not found.
    /// </summary>
    Task<PlannedBlock?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a new planned block.
    /// </summary>
    Task AddAsync(
        PlannedBlock block,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists changes to an existing planned block.
    /// </summary>
    Task UpdateAsync(
        PlannedBlock block,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a planned block — add or update.
    /// </summary>
    Task SaveAsync(
        PlannedBlock block,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a planned block by Id.
    /// </summary>
    Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if any planned block after <paramref name="after"/> references the given profile.
    /// Used to guard delete/update operations.
    /// </summary>
    Task<bool> HasFutureBlocksForProfileAsync(
        Guid profileId,
        DateTime after,
        CancellationToken cancellationToken = default);
}