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
    public Moment StartedAt { get; init; }
    public Moment EndAt { get; init; }
    public Moment? EndedAt { get; init; }

    /// <summary>The session's break schedule (offsets from StartedAt) — empty for "No
    /// timer". Everything live about breaks is read off this against the clock.</summary>
    public IReadOnlyList<SessionBreakDto> Breaks { get; init; } = [];
    public string? BreakPresetName { get; init; }

    /// <summary>Whether this session's preset stays blocked through its breaks (Gap 64).</summary>
    public bool KeepBlockingDuringBreaks { get; init; }

    public static IReadOnlyList<SessionBreakDto> MapBreaks(Domain.ValueObjects.BreakSchedule schedule) =>
        schedule.Breaks
            .Select(b => new SessionBreakDto { Kind = b.Kind, Start = b.Start, End = b.End })
            .ToList();
}
