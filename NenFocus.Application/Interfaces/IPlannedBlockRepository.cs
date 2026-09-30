using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

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
    /// Returns all non-cancelled planned blocks whose scheduled slot overlaps <paramref name="range"/>, by start.
    /// </summary>
    Task<IReadOnlyList<PlannedBlock>> GetForRangeAsync(
        TimeSlot range,
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
        Moment after,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns Focus blocks whose window has closed (End &lt; asOf) without the
    /// ritual ever being completed. Used by the missed-session sweep on startup
    /// and on live enforcement release.
    /// </summary>
    Task<IReadOnlyList<FocusBlock>> GetUnresolvedFocusBlocksAsync(
        Moment asOf,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all planned blocks linked to the given Nen Contract.
    /// </summary>
    Task<IReadOnlyList<PlannedBlock>> GetForContractAsync(
        Guid contractId,
        CancellationToken cancellationToken = default);
}