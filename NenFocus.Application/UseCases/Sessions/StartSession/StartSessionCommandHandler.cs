using FluentValidation;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Aggregates;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DomainSession = NenFocus.Domain.Entities.Session;

namespace NenFocus.Application.UseCases.Sessions.StartSession;

public sealed class StartSessionCommandHandler : ICommandHandler<StartSessionCommand, StartSessionResult>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockProfileRepository _profiles;
    private readonly IEnforcementService _enforcement;
    private readonly INotificationService _notifications;
    private readonly ISessionStateService _sessionState;
    private readonly IValidator<StartSessionCommand> _validator;

    public StartSessionCommandHandler(
        ISessionRepository sessions,
        IBlockProfileRepository profiles,
        IEnforcementService enforcement,
        INotificationService notifications,
        ISessionStateService sessionState,
        IValidator<StartSessionCommand> validator)
    {
        _sessions = sessions;
        _profiles = profiles;
        _enforcement = enforcement;
        _notifications = notifications;
        _sessionState = sessionState;
        _validator = validator;
    }

    public async Task<StartSessionResult> HandleAsync(StartSessionCommand command, CancellationToken ct = default)
    {
        var validationResult = await _validator.ValidateAsync(command, ct);
        if (!validationResult.IsValid)
            return StartSessionResult.Fail(validationResult.Errors.First().ErrorMessage);

        if (await _sessions.HasActiveSessionAsync(ct))
            return StartSessionResult.Fail("A session is already active.");

        BlockProfile? profile = null;
        if (command.ProfileId.HasValue)
        {
            profile = await _profiles.GetByIdAsync(command.ProfileId.Value, ct);
            if (profile is null) return StartSessionResult.Fail("Profile not found.");
        }

        var session = DomainSession.Create(
            goal: new SessionGoal(command.Goal),
            duration: new SessionDuration(TimeSpan.FromMinutes(command.DurationMinutes)),
            blockProfileId: command.ProfileId ?? Guid.Empty,
            breakSettings: command.BreakSettings?.Enabled == true
                ? new BreakSettings(command.BreakSettings.IntervalMinutes, command.BreakSettings.BreakDurationMinutes)
                : null);

        session.Start();

        var policy = BuildPolicySlice(profile, command, session);
        await _enforcement.ApplyPolicyAsync(policy, ct);

        if (command.BreakSettings?.Enabled == true)
        {
            await _notifications.ScheduleBreakReminderAsync(
                TimeSpan.FromMinutes(command.BreakSettings.IntervalMinutes),
                TimeSpan.FromMinutes(command.BreakSettings.BreakDurationMinutes), ct);
        }

        await _sessions.SaveAsync(session, ct);

        _sessionState.SetActive(new SessionDto
        {
            Id = session.Id,
            Goal = command.Goal,
            ProfileName = profile?.Name ?? "No Profile",
            Status = "Active",
            StartedAt = session.StartedAt!.Value,
            DurationMinutes = command.DurationMinutes
        });

        return StartSessionResult.Ok(session.Id);
    }

    private static PolicySliceDto BuildPolicySlice(BlockProfile? profile, StartSessionCommand cmd, DomainSession session)
    {
        var blockedApps = (profile?.BlockedApps.Select(a => a.ProcessName) ?? Enumerable.Empty<string>())
            .Union(cmd.TemporaryBlockedApps, StringComparer.OrdinalIgnoreCase).ToHashSet();

        var mode = profile?.WebsitePolicy ?? WebsitePolicy.Blocklist;
        var profileSites = profile?.Websites.Select(s => s.Url) ?? Enumerable.Empty<string>();

        return new PolicySliceDto
        {
            ActiveSessionId = session.Id,
            WebsitePolicyMode = mode,
            AllowedSites = mode == WebsitePolicy.Allowlist ? profileSites.ToHashSet() : [],
            BlockedSites = mode == WebsitePolicy.Blocklist ? profileSites.Union(cmd.TemporaryBlockedSites).ToHashSet() : [],
            BlockedApps = blockedApps,
            SliceStart = session.StartedAt!.Value,
            SliceEnd = session.StartedAt!.Value.AddMinutes(cmd.DurationMinutes)
        };
    }
}