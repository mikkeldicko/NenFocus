using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Notifications;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

public sealed class SessionEndedEventHandler : IDomainEventHandler<SessionEndedEvent>
{
    private readonly IEnforcementSynchronizer _synchronizer;
    private readonly INotifier _notifier;
    private readonly IActiveSessionState _sessionState;
    private readonly ISessionRepository _sessions;
    private readonly IBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly IBlockedAttemptRepository _attempts;
    private readonly IRecapRelay _recaps;
    private readonly ISystemClock _clock;

    public SessionEndedEventHandler(
        IEnforcementSynchronizer synchronizer,
        INotifier notifier,
        IActiveSessionState sessionState,
        ISessionRepository sessions,
        IBlockRepository blocks,
        IBlockPresetRepository presets,
        IBlockedAttemptRepository attempts,
        IRecapRelay recaps,
        ISystemClock clock)
    {
        _synchronizer = synchronizer;
        _notifier = notifier;
        _sessionState = sessionState;
        _sessions = sessions;
        _blocks = blocks;
        _presets = presets;
        _attempts = attempts;
        _recaps = recaps;
        _clock = clock;
    }

    public async Task HandleAsync(
        SessionEndedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        // First, and only after the DB commit (EfCoreUnitOfWork.CommitAsync publishes after
        // SaveChangesAsync): the refresh reads the session as ended, so the UI's view and the
        // database can't disagree (Gap 92, A3).
        await _sessionState.RefreshAsync(cancellationToken);

        // Sync, never "remove everything" — a Background Block still running must stay
        // enforced after the session ends (Gap 62).
        await _synchronizer.SyncAsync(_clock.Now, cancellationToken);

        // The session owns a planned block's ending: the block never announces it
        // (FocusBlock.AnnouncesOwnEnd), so this is the one toast for that moment.
        var session = await _sessions.GetByIdAsync(domainEvent.SessionId, cancellationToken);
        var block = session?.OriginatingBlockId is { } blockId
            ? await _blocks.GetByIdAsync(blockId, cancellationToken)
            : null;
        var preset = block is null ? null : await _presets.GetByIdAsync(block.BlockPresetId, cancellationToken);
        var blockName = block?.DisplayName(preset?.Name ?? "");

        await _notifier.NotifyAsync(new SessionCompleted(block is not null, blockName), cancellationToken);

        // The one path to the Post-Session screen, whoever ended it — you or the clock (Gap 89
        // Slice A). Built from what was saved, after the commit (Gap 110).
        if (session is null) return;
        var attempts = await _attempts.GetForSessionAsync(session.Id, cancellationToken);
        var sessionPreset = await _presets.GetByIdAsync(session.BlockPresetId, cancellationToken);
        _recaps.NotifyPostSessionsReady([PostSessionDto.From(session, block as FocusBlock, sessionPreset?.Name ?? string.Empty, attempts)]);
    }
}
