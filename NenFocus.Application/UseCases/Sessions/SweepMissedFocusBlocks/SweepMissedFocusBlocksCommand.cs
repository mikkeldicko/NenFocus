using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Sessions.SweepMissedFocusBlocks;

public sealed record SweepMissedFocusBlocksCommand : ICommand<SweepMissedFocusBlocksResult>
{
    /// <summary>
    /// The "now" to sweep against — null falls back to ISystemClock.Now read fresh
    /// inside the handler. A caller that already captured "now" for other checks in the
    /// same operation (e.g. ScheduledEnforcementService's heartbeat, which also feeds it to
    /// ComputeActivePolicySliceQuery) should always supply it explicitly, so the sweep and
    /// those other checks can never disagree about what moment "now" means.
    /// </summary>
    public Moment? AsOf { get; init; }
}
