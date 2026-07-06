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
    private readonly IBlockProfileRepository _profiles;

    public MarkFocusBlockMissedCommandHandler(
        IPlannedBlockRepository blocks,
        ISessionRepository sessions,
        IBlockProfileRepository profiles)
    {
        _blocks = blocks;
        _sessions = sessions;
        _profiles = profiles;
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
            blockProfileId: block.BlockProfileId,
            origin: SessionOrigin.Planned,
            originatingBlockId: block.Id);

        session.Miss(block.Start, block.End);

        await _sessions.SaveAsync(session, ct);

        var profileName = string.Empty;
        if (block.BlockProfileId != Guid.Empty)
        {
            var profile = await _profiles.GetByIdAsync(block.BlockProfileId, ct);
            profileName = profile?.Name ?? string.Empty;
        }

        var postSession = new PostSessionDto
        {
            SessionId = session.Id,
            Status = session.Status,
            Goal = session.Goal.Value,
            ProfileName = profileName,
            StartedAt = session.StartedAt!.Value,
            EndedAt = session.EndedAt!.Value,
            PlannedDurationMinutes = (int)session.Duration.Value.TotalMinutes,
            ActualDurationMinutes = (int)(session.GetElapsedTime(session.EndedAt.Value)?.TotalMinutes ?? 0),
            BreaksTaken = 0
        };

        return MarkFocusBlockMissedResult.Ok(session.Id, postSession);
    }
}
