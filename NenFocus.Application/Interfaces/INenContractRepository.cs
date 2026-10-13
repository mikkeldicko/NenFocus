using NenFocus.Domain.Entities;

namespace NenFocus.Application.Interfaces;

public interface INenContractRepository
{
    Task<NenContract?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Returns every contract regardless of status (Scheduled, Active, Completed, Broken).
    /// Backs Insights-facing reads — a broken-contract count, and the full contract list
    /// Bonus Block resolution checks a block's Start date against (DESIGN_GAP_HISTORY.md
    /// Gap 39). Unlike GetAllInEffectAsync, deliberately unfiltered by lifecycle status.
    /// </summary>
    Task<IReadOnlyList<NenContract>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every Completed or Broken contract whose Post-Contract recap screen has
    /// never been dismissed (PostContractAcknowledgedAt is null) — mirrors
    /// ISessionRepository.GetUnacknowledgedPostSessionsAsync exactly, same idiom.
    /// </summary>
    Task<IReadOnlyList<NenContract>> GetUnacknowledgedPostContractsAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a new contract; it is saved when the command commits (Gap 110).</summary>
    void Add(NenContract contract);

    /// <summary>Removes a loaded contract; saved when the command commits. Ask first whether it may go.</summary>
    void Remove(NenContract contract);
}
