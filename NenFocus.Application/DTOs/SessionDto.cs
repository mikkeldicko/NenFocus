using NenFocus.Domain.ValueObjects;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

public sealed record SessionDto
{
    public Guid Id { get; init; }
    public string Goal { get; init; } = string.Empty;
    public string PresetName { get; init; } = string.Empty;
    public int DurationMinutes { get; init; }
    public SessionStatus Status { get; init; }
    public SessionOrigin Origin { get; init; }
    public Guid BlockPresetId { get; init; }
    public Guid? OriginatingBlockId { get; init; }
    public Moment? EndedAt { get; init; }

    /// <summary>
    /// The session's run, breaks and keep-blocking rule — the same <see cref="SessionTimeline"/> the
    /// session answers from (Gap 92, A4), so the Main page reads exactly what enforcement reads.
    /// Null before the session has started.
    /// </summary>
    public SessionTimeline? Timeline { get; init; }
    public string? BreakPresetName { get; init; }
}
