namespace NenFocus.Application.DTOs;

public sealed record BreakSettingsDto
{
    public bool Enabled { get; init; }

    /// <summary>Typical values: 25, 45, 60.</summary>
    public int IntervalMinutes { get; init; }

    /// <summary>Typical values: 5, 10, 15.</summary>
    public int BreakDurationMinutes { get; init; }
}
