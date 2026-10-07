using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Interfaces;

/// <summary>
/// Which Focus block, if any, awaits its Ritual (GetFocusBlockAwaitingRitualQuery). Only that:
/// whether a block stopped awaiting because its window closed or because its session started is
/// not the prompt's business — a missed block is announced by the heartbeat's sweep (Gap 92, A3, D′).
/// The heartbeat says when to ask, so every tick's questions share one "now" in a fixed order.
/// </summary>
public interface IRitualPrompt
{
    Guid? PendingFocusBlockId { get; }

    /// <summary>Raised with the block now awaiting its Ritual (null: none), only when that changes.</summary>
    event EventHandler<Guid?>? Changed;

    Task RefreshAsync(Moment at, CancellationToken cancellationToken = default);
}
