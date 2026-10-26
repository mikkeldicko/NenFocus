using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.UseCases.Sessions.AutoEndSession;

public sealed class AutoEndSessionCommandHandler
    : ICommandHandler<AutoEndSessionCommand, AutoEndSessionResult>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockRepository _blocks;

    public AutoEndSessionCommandHandler(ISessionRepository sessions, IBlockRepository blocks)
    {
        _sessions = sessions;
        _blocks = blocks;
    }

    public async Task<AutoEndSessionResult> HandleAsync(
        AutoEndSessionCommand command,
        CancellationToken ct = default)
    {
        var session = await _sessions.GetActiveAsync(ct);
        if (session is null || !session.IsDueForAutoEnd(command.AsOf))
            return new AutoEndSessionResult();

        var originatingFocusBlock = session.OriginatingBlockId is { } blockId
            ? await _blocks.GetByIdAsync(blockId, ct) as FocusBlock
            : null;

        // The same ending as EndSession: capped at the planned end, the block stamped with it.
        session.Complete(command.AsOf, originatingFocusBlock);

        return new AutoEndSessionResult { EndedSessionId = session.Id };
    }
}
