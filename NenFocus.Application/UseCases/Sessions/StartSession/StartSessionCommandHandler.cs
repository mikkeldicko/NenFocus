using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Sessions.StartSession;

public sealed class StartSessionCommandHandler
    : ICommandHandler<StartSessionCommand, StartSessionResult>
{
    private readonly ISessionRepository _sessions;
    private readonly IPlannedBlockRepository _plannedBlocks;
    private readonly ISessionStateService _sessionState;
    private readonly IBlockPresetRepository _presets;
    private readonly ISystemClock _clock;

    public StartSessionCommandHandler(
        ISessionRepository sessions,
        IPlannedBlockRepository plannedBlocks,
        ISessionStateService sessionState,
        IBlockPresetRepository presets,
        ISystemClock clock)
    {
        _sessions = sessions;
        _plannedBlocks = plannedBlocks;
        _sessionState = sessionState;
        _presets = presets;
        _clock = clock;
    }

    public async Task<StartSessionResult> HandleAsync(
        StartSessionCommand command,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var requestedEnd = now.AddMinutes(command.DurationMinutes);

        var upcomingBlocks = await _plannedBlocks.GetForRangeAsync(now, requestedEnd, ct);
        var conflictingBlock = upcomingBlocks
            .OfType<FocusBlock>()
            .Where(b => b.Id != command.OriginatingBlockId)
            .OrderBy(b => b.Start)
            .FirstOrDefault();

        if (conflictingBlock is not null)
        {
            var minutesAvailable = conflictingBlock.MinutesUntilStart(now);
            if (minutesAvailable > 0)
                return StartSessionResult.Fail(
                    $"A planned focus block starts in {minutesAvailable} minutes. " +
                    $"Please set your duration to {minutesAvailable} minutes or less.");

            return StartSessionResult.Fail(
                "A focus block is currently active. Please complete the Ritual instead of starting a spontaneous session.");
        }

        FocusBlock? originatingBlock = null;
        if (command.OriginatingBlockId.HasValue)
        {
            originatingBlock = await _plannedBlocks.GetByIdAsync(command.OriginatingBlockId.Value, ct) as FocusBlock;

            if (originatingBlock is null)
                return StartSessionResult.Fail("The originating focus block was not found.");

            if (now >= originatingBlock.End)
                return StartSessionResult.Fail(
                    "This focus block's window has already closed. It will be recorded as missed instead.");

            if (await _sessions.ExistsForOriginatingBlockAsync(command.OriginatingBlockId.Value, ct))
                return StartSessionResult.Fail("A session already exists for this focus block.");
        }

        BreakSettings? breakSettings = null;
        if (command.BreakSettings is not null)
        {
            breakSettings = new BreakSettings(
                command.BreakSettings.IntervalMinutes,
                command.BreakSettings.BreakDurationMinutes);
        }

        var origin = command.OriginatingBlockId.HasValue
            ? SessionOrigin.Planned
            : SessionOrigin.Spontaneous;

        var session = Session.Create(
            id: Guid.NewGuid(),
            goal: new SessionGoal(command.Goal),
            duration: new SessionDuration(TimeSpan.FromMinutes(command.DurationMinutes)),
            blockPresetId: command.PresetId ?? Guid.Empty,
            origin: origin,
            originatingBlockId: command.OriginatingBlockId,
            breakSettings: breakSettings,
            temporaryBlockedApps: command.AdditionalBlockedApps.Count > 0 ? command.AdditionalBlockedApps : null,
            temporaryBlockedSites: command.AdditionalBlockedSites.Count > 0 ? command.AdditionalBlockedSites : null);

        session.Start(now);

        if (originatingBlock is not null)
        {
            originatingBlock.MarkRitualCompleted(now);
            await _plannedBlocks.SaveAsync(originatingBlock, ct);
        }

        await _sessions.SaveAsync(session, ct);

        var presetName = string.Empty;
        if (command.PresetId is { } pid && pid != Guid.Empty)
        {
            var preset = await _presets.GetByIdAsync(pid, ct);
            presetName = preset?.Name ?? string.Empty;
        }

        _sessionState.SetActive(new SessionDto
        {
            Id = session.Id,
            Goal = session.Goal.Value,
            PresetName = presetName,
            BlockPresetId = session.BlockPresetId,
            DurationMinutes = command.DurationMinutes,
            StartedAt = session.StartedAt ?? now,
            Status = session.Status,
            Origin = session.Origin,
            OriginatingBlockId = session.OriginatingBlockId,
            BreakSettings = command.BreakSettings
        });

        return StartSessionResult.Ok(session.Id);
    }
}
