using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Sessions.SweepMissedFocusBlocks;

public sealed record SweepMissedFocusBlocksCommand : ICommand<SweepMissedFocusBlocksResult>
{
    /// <summary>The heartbeat tick's one "now" — required, so the sweep and the tick's other steps
    /// can never disagree about what moment "now" means (Gap 21; Gap 89 Slice A).</summary>
    public required Moment AsOf { get; init; }
}
