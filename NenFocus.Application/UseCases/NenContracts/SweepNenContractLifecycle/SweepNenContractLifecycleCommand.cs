using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.NenContracts.SweepNenContractLifecycle;

/// <summary>
/// Activates Scheduled contracts whose start has arrived and completes Active contracts whose
/// end has passed — one heartbeat tick, both housekeeping checks. Mirrors
/// SweepMissedFocusBlocksCommand's shape.
/// </summary>
public sealed record SweepNenContractLifecycleCommand : ICommand<SweepNenContractLifecycleResult>
{
    /// <summary>The heartbeat tick's one "now" — required, so the sweep can't read a moment of its
    /// own that disagrees with the tick's other steps (Gap 89 Slice A, F4).</summary>
    public required Moment AsOf { get; init; }
}
