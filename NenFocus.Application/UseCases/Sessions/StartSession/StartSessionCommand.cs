using NenFocus.Application.DTOs;
using System;
using System.Collections.Generic;

namespace NenFocus.Application.UseCases.Sessions.StartSession;

public sealed record StartSessionCommand
{
    public string Goal { get; init; } = string.Empty;
    public Guid? ProfileId { get; init; }
    public int DurationMinutes { get; init; }
    public BreakSettingsDto? BreakSettings { get; init; }
    public IReadOnlyList<string> TemporaryBlockedApps { get; init; } = [];
    public IReadOnlyList<string> TemporaryBlockedSites { get; init; } = [];
    public bool IsPlanned { get; init; }
}