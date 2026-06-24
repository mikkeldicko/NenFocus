using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

public sealed record SessionDto
{
    public Guid Id { get; init; }
    public string Goal { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public int DurationMinutes { get; init; }
    public SessionStatus Status { get; init; }
    public SessionOrigin Origin { get; init; }
    public Guid BlockProfileId { get; init; }
    public Guid? OriginatingBlockId { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public BreakSettingsDto? BreakSettings { get; init; }
    public int BreaksTaken { get; init; }
    public DateTime? BreakStartedAt { get; init; }
}