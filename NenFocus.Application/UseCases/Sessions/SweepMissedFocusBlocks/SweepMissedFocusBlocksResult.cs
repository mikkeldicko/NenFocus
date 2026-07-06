using NenFocus.Application.DTOs;

namespace NenFocus.Application.UseCases.Sessions.SweepMissedFocusBlocks;

public sealed record SweepMissedFocusBlocksResult
{
    public IReadOnlyList<PostSessionDto> MissedSessions { get; init; } = [];
}
