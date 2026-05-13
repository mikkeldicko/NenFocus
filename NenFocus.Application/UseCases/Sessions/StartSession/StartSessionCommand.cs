using NenFocus.Application.DTOs;

namespace NenFocus.Application.UseCases.Sessions.StartSession;

public sealed record StartSessionCommand
{
    public string Goal { get; init; } = string.Empty;
    public Guid? ProfileId { get; init; }
    public int DurationMinutes { get; init; }
    public BreakSettingsDto? BreakSettings { get; init; }
    public IReadOnlyList<string> TemporaryBlockedApps { get; init; } = [];
    public IReadOnlyList<string> TemporaryBlockedSites { get; init; } = [];

    /// <summary>
    /// Set by the Hatsu page when a RitualBlock triggered the Ritual.
    /// Null for Impromptu sessions.
    /// </summary>
    public Guid? OriginatingBlockId { get; init; }
}