using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.StartSession;

public sealed record StartSessionCommand : ICommand<StartSessionResult>
{
    public string Goal { get; init; } = string.Empty;
    public Guid? ProfileId { get; init; }
    public int DurationMinutes { get; init; }
    public BreakSettingsDto? BreakSettings { get; init; }
    public Guid? OriginatingBlockId { get; init; }
    public IReadOnlyList<string> AdditionalBlockedSites { get; init; } = [];
    public IReadOnlyList<string> AdditionalBlockedApps { get; init; } = [];
}
