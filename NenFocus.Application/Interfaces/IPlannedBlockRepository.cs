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
    /// Returns true if any planned block after <paramref name="after"/> references the given preset.
    /// Used to guard delete/update operations.
    /// </summary>
    Task<bool> HasFutureBlocksForPresetAsync(
        Guid presetId,
        DateTime after,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if a FocusBlock is active at the given point in time.
    /// </summary>
    Task<bool> HasActiveFocusBlockAsync(
        DateTime at,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns Focus blocks whose window has closed (End &lt; asOf) without the
    /// ritual ever being completed. Used by the missed-session sweep on startup
    /// and on live enforcement release.
    /// </summary>
    Task<IReadOnlyList<FocusBlock>> GetUnresolvedFocusBlocksAsync(
        DateTime asOf,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all planned blocks linked to the given Nen Contract.
    /// </summary>
    Task<IReadOnlyList<PlannedBlock>> GetForContractAsync(
        Guid contractId,
        CancellationToken cancellationToken = default);
}