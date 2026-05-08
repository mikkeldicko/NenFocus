    namespace NenFocus.Application.DTOs;

/// <summary>
/// Break reminder configuration chosen on the Hatsu page.
/// Carried inside SessionDto and StartSessionCommand.
/// </summary>
public sealed record BreakSettingsDto
{
    public bool Enabled { get; init; }

    /// <summary>Minutes between break reminders. Typical values: 25, 45, 60.</summary>
    public int IntervalMinutes { get; init; }

    /// <summary>Duration of each break in minutes. Typical values: 5, 10, 15.</summary>
    public int BreakDurationMinutes { get; init; }
}
