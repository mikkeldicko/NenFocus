using NenFocus.Domain.ValueObjects;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

/// <summary>
/// Session-side facts for a done Focus block's Planner snapshot view — Zetsu goal, reflection,
/// blocked-attempt history, and any deficit this block redeemed. Returned by
/// GetSessionSnapshotForBlockQueryHandler; null means no Session was ever tied to the block
/// (Missed, Cancelled, or a Background block, which has no Ritual/Session at all).
/// </summary>
public sealed record SessionSnapshotDto
{
    public string Goal { get; init; } = string.Empty;
    public string? Reflection { get; init; }
    public IReadOnlyList<BlockedAttemptSnapshotDto> Attempts { get; init; } = Array.Empty<BlockedAttemptSnapshotDto>();
    public IReadOnlyList<RedemptionCoverageDto> RedemptionCoverage { get; init; } = Array.Empty<RedemptionCoverageDto>();
}

public sealed record BlockedAttemptSnapshotDto
{
    public string ResourceName { get; init; } = string.Empty;
    public BlockedAttemptType AttemptType { get; init; }
    public Moment OccurredAt { get; init; }
}

public sealed record RedemptionCoverageDto
{
    public Guid MissedBlockId { get; init; }
    public string MissedBlockPresetName { get; init; } = string.Empty;
    public DateOnly MissedBlockDate { get; init; }
    public double MinutesCovered { get; init; }
}
