using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.StartSession;

public sealed record StartSessionCommand : ICommand<StartSessionResult>
{
    public string Goal { get; init; } = string.Empty;
    public Guid? ProfileId { get; init; }
    public int DurationMinutes { get; init; }
    public BreakSettingsDto? BreakSettings { get; init; }
    public IReadOnlyList<string> TemporaryBlockedApps { get; init; } = [];
    public IReadOnlyList<string> TemporaryBlockedSites { get; init; } = [];

    public Guid? OriginatingBlockId { get; init; }
}