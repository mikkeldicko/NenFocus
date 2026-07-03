using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

public sealed record PlannedBlockDto
{
    public Guid Id { get; init; }
    public Guid ProfileId { get; init; }
    public string ProfileName { get; init; } = string.Empty;

    public BlockType BlockType { get; init; }
    public WebsitePolicy WebsitePolicy { get; init; }

    public DateTime Start { get; init; }
    public DateTime End { get; init; }

    public bool IsCompleted { get; init; }

    public int DurationMinutes => (int)(End - Start).TotalMinutes;
}
