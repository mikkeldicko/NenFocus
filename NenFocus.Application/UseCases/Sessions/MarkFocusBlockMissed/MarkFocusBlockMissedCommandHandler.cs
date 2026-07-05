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

    public MarkFocusBlockMissedCommandHandler(
        IPlannedBlockRepository blocks,
        ISessionRepository sessions)
    {
        _blocks = blocks;
        _sessions = sessions;
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
        return MarkFocusBlockMissedResult.Ok(session.Id);
    }
}
