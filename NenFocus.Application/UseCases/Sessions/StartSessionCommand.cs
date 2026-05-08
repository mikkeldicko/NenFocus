using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Aggregates;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using DomainSession = NenFocus.Domain.Entities.Session;

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

public sealed record StartSessionResult
{
    public bool Success { get; init; }
    public Guid? SessionId { get; init; }
    public string? ErrorMessage { get; init; }

    public static StartSessionResult Ok(Guid sessionId) =>
        new() { Success = true, SessionId = sessionId };
    public static StartSessionResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}

public sealed class StartSessionCommandHandler
    : ICommandHandler<StartSessionCommand, StartSessionResult>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockProfileRepository _profiles;
    private readonly IEnforcementService _enforcement;
    private readonly INotificationService _notifications;
    private readonly ISessionStateService _sessionState;

    public StartSessionCommandHandler(
        ISessionRepository sessions,
        IBlockProfileRepository profiles,
        IEnforcementService enforcement,
        INotificationService notifications,
        ISessionStateService sessionState)
    {
        _sessions = sessions;
        _profiles = profiles;
        _enforcement = enforcement;
        _notifications = notifications;
        _sessionState = sessionState;
    }

    public async Task<StartSessionResult> HandleAsync(
        StartSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (await _sessions.HasActiveSessionAsync(cancellationToken))
            return StartSessionResult.Fail("A session is already active.");

        BlockProfile? profile = null;
        if (command.ProfileId.HasValue)
        {
            profile = await _profiles.GetByIdAsync(command.ProfileId.Value, cancellationToken);
            if (profile is null)
                return StartSessionResult.Fail($"Profile '{command.ProfileId}' not found.");
        }

        var goal = new SessionGoal(command.Goal);
        var duration = new SessionDuration(TimeSpan.FromMinutes(command.DurationMinutes));

        BreakSettings? breakSettings = null;
        if (command.BreakSettings?.Enabled == true)
        {
            breakSettings = new BreakSettings(
                intervalMinutes: command.BreakSettings.IntervalMinutes,
                breakLengthMinutes: command.BreakSettings.BreakDurationMinutes);
        }

        var session = DomainSession.Create(
            goal: goal,
            duration: duration,
            blockProfileId: command.ProfileId ?? Guid.Empty,
            breakSettings: breakSettings);

        session.Start();

        var policy = BuildPolicySlice(profile, command, session);
        await _enforcement.ApplyPolicyAsync(policy, cancellationToken);

        if (command.BreakSettings?.Enabled == true)
        {
            await _notifications.ScheduleBreakReminderAsync(
                TimeSpan.FromMinutes(command.BreakSettings.IntervalMinutes),
                TimeSpan.FromMinutes(command.BreakSettings.BreakDurationMinutes),
                cancellationToken);
        }

        await _sessions.SaveAsync(session, cancellationToken);

        // Notify the UI layer that a session is now active.
        _sessionState.SetActive(new SessionDto
        {
            Id = session.Id,
            Goal = command.Goal,
            ProfileId = command.ProfileId?.ToString() ?? string.Empty,
            ProfileName = profile?.Name ?? string.Empty,
            DurationMinutes = command.DurationMinutes,
            Status = "Active",
            StartedAt = session.StartedAt!.Value,
            BreakSettings = command.BreakSettings
        });

        return StartSessionResult.Ok(session.Id);
    }

    private static PolicySliceDto BuildPolicySlice(
        BlockProfile? profile,
        StartSessionCommand command,
        DomainSession session)
    {
        var profileApps = profile?.BlockedApps.Select(a => a.ProcessName)
                          ?? Enumerable.Empty<string>();
        var blockedApps = profileApps
            .Union(command.TemporaryBlockedApps, StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var isAllowlist = profile?.WebsitePolicy == WebsitePolicy.Allowlist;

        HashSet<string> allowedSites = [];
        HashSet<string> blockedSites = [];

        if (isAllowlist)
        {
            allowedSites = profile!.Websites
                .Select(s => s.Url)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            var profileSites = profile?.Websites.Select(s => s.Url)
                               ?? Enumerable.Empty<string>();
            blockedSites = profileSites
                .Union(command.TemporaryBlockedSites, StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return new PolicySliceDto
        {
            SliceStart = session.StartedAt!.Value,
            SliceEnd = session.StartedAt!.Value.AddMinutes(command.DurationMinutes),
            WebsiteAllowlistMode = isAllowlist,
            AllowedSites = allowedSites,
            BlockedSites = blockedSites,
            BlockedApps = blockedApps
        };
    }
}