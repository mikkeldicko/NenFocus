using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Interfaces;

public interface IPlannedBlockRepository
{
    /// <summary>
    /// Returns all non-cancelled planned blocks whose scheduled slot overlaps <paramref name="range"/>, by start.
    /// For "the blocks on these days", the caller asks <c>UserTimeZone.SlotCovering</c> for the range.
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

    /// <summary>Adds a new planned block; it is saved when the command commits (Gap 110).</summary>
    void Add(PlannedBlock block);

    /// <summary>Removes a loaded planned block; saved when the command commits. Ask first whether it may go.</summary>
    void Remove(PlannedBlock block);
}
