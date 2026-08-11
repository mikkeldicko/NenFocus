using NenFocus.Domain.Entities;

namespace NenFocus.Application.Interfaces;

public interface INenContractRepository
{
    Task<NenContract?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(NenContract contract, CancellationToken cancellationToken = default);

    Task UpdateAsync(NenContract contract, CancellationToken cancellationToken = default);

    /// <summary>Saves a Nen Contract — add or update.</summary>
    Task SaveAsync(NenContract contract, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns contracts for which NenContract.IsDueForActivation(asOf) is true — that
    /// method is the canonical definition; the EF Core query must match it exactly, not
    /// reinvent the comparison. Mirrors GetUnresolvedFocusBlocksAsync's shape.
    /// </summary>
    Task<IReadOnlyList<NenContract>> GetDueForActivationAsync(
        DateTime asOf,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns contracts for which NenContract.IsDueForCompletion(asOf) is true — same
    /// canonical-definition rule as GetDueForActivationAsync.
    /// </summary>
    Task<IReadOnlyList<NenContract>> GetDueForCompletionAsync(
        DateTime asOf,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if any contract sealed from the given Contract Preset is currently
    /// NenContract.IsInEffect (Scheduled or Active) — that method is the canonical
    /// definition; this query must match it exactly, not reinvent it. Used to block
    /// deletion of a Contract Preset that a live commitment was made from.
    /// </summary>
    Task<bool> HasContractsInEffectForPresetAsync(
        Guid contractPresetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every contract for which NenContract.IsInEffect is true (Scheduled or
    /// Active) — nothing currently prevents more than one from being in effect at once.
    /// Backs both the Planner's active-contract banner (filtered/tie-broken per viewed
    /// week by the caller) and the contract-overlap check at wizard-Basics-step and Seal
    /// time. Callers own whatever selection/filtering they need; this returns the raw set.
    /// </summary>
    Task<IReadOnlyList<NenContract>> GetAllInEffectAsync(CancellationToken cancellationToken = default);
}
