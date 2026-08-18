using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Sessions.MarkFocusBlockMissed;

public sealed class MarkFocusBlockMissedCommandHandler
    : ICommandHandler<MarkFocusBlockMissedCommand, MarkFocusBlockMissedResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISessionRepository _sessions;
    private readonly IBlockPresetRepository _presets;

    public MarkFocusBlockMissedCommandHandler(
        IPlannedBlockRepository blocks,
        ISessionRepository sessions,
        IBlockPresetRepository presets)
    {
        _blocks = blocks;
        _sessions = sessions;
        _presets = presets;
    }

    public async Task<MarkFocusBlockMissedResult> HandleAsync(
        MarkFocusBlockMissedCommand command,
        CancellationToken ct = default)
    {
        var block = await _blocks.GetByIdAsync(command.FocusBlockId, ct) as FocusBlock;
        if (block is null)
            return MarkFocusBlockMissedResult.Fail("Focus block not found.");

        var session = Session.Create(
            id: Guid.NewGuid(),
            goal: new SessionGoal("(missed — no goal set)"),
            duration: new SessionDuration(block.GetDuration()),
            blockPresetId: block.BlockPresetId,
            origin: SessionOrigin.Planned,
            originatingBlockId: block.Id);

        session.Miss(block.Start, block.End);

        await _sessions.SaveAsync(session, ct);

        var presetName = string.Empty;
        if (block.BlockPresetId != Guid.Empty)
        {
            var preset = await _presets.GetByIdAsync(block.BlockPresetId, ct);
            presetName = preset?.Name ?? string.Empty;
        }

        var postSession = new PostSessionDto
        {
            SessionId = session.Id,
            Status = session.Status,
            Goal = session.Goal.Value,
            PresetName = presetName,
            StartedAt = session.StartedAt!.Value,
            EndedAt = session.EndedAt!.Value,
            PlannedDurationMinutes = session.Duration.Value.TotalMinutes,
            ActualDurationMinutes = session.GetElapsedTime(session.EndedAt.Value)?.TotalMinutes ?? 0,
            BreaksTaken = 0
        };

        return MarkFocusBlockMissedResult.Ok(session.Id, postSession);
    }
}
