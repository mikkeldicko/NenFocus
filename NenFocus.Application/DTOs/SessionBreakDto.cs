using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

/// <summary>Application-layer mirror of one Domain SessionBreak: offsets from the
/// session's StartedAt (Gap 63).</summary>
public sealed record SessionBreakDto
{
    public SessionPhaseKind Kind { get; init; }
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
}
