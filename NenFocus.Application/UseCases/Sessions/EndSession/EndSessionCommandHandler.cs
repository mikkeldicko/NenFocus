using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Sessions.EndSession;

public sealed class EndSessionCommandHandler
    : ICommandHandler<EndSessionCommand, EndSessionResult>
{
    private readonly ISessionRepository _sessions;
    private readonly IPlannedBlockRepository _plannedBlocks;
    private readonly ISystemClock _clock;

    public EndSessionCommandHandler(
        ISessionRepository sessions,
        IPlannedBlockRepository plannedBlocks,
        ISystemClock clock)
    {
        _sessions = sessions;
        _plannedBlocks = plannedBlocks;
        _clock = clock;
    }

    public async Task<EndSessionResult> HandleAsync(
        EndSessionCommand command,
        CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(command.SessionId, ct);
        if (session is null)
            return EndSessionResult.Fail($"Session '{command.SessionId}' not found.");

        if (session.Status != SessionStatus.Active)
            return EndSessionResult.Fail("Session is not currently active.");

        var originatingFocusBlock = session.OriginatingBlockId is { } blockId
            ? await _plannedBlocks.GetByIdAsync(blockId, ct) as FocusBlock
            : null;

        // The one ending (Gap 89 Slice A): capped at the planned end, and the block stamped with
        // that same end (Gaps 63, 86). Its Post-Session screen follows from SessionEndedEventHandler,
        // after the commit.
        session.Complete(_clock.Now, originatingFocusBlock);

        return EndSessionResult.Ok();
    }
}
