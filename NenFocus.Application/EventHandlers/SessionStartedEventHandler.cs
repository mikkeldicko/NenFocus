using NenFocus.Application.Interfaces;
using NenFocus.Application.Notifications;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

public sealed class SessionStartedEventHandler : IDomainEventHandler<SessionStartedEvent>
{
    private readonly IEnforcementSynchronizer _synchronizer;
    private readonly INotifier _notifier;
    private readonly IActiveSessionState _sessionState;
    private readonly ISessionRepository _sessions;
    private readonly IBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly ISystemClock _clock;

    public SessionStartedEventHandler(
        IEnforcementSynchronizer synchronizer,
        INotifier notifier,
        IActiveSessionState sessionState,
        ISessionRepository sessions,
        IBlockRepository blocks,
        IBlockPresetRepository presets,
        ISystemClock clock)
    {
        _synchronizer = synchronizer;
        _notifier = notifier;
        _sessionState = sessionState;
        _sessions = sessions;
        _blocks = blocks;
        _presets = presets;
        _clock = clock;
    }

    public async Task HandleAsync(
        SessionStartedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        // First: the app's view of the running session reads it from the database, now committed
        // (Gap 92, A3 — StartSession no longer pushes a copy).
        await _sessionState.RefreshAsync(cancellationToken);

        await _synchronizer.SyncAsync(_clock.Now, cancellationToken);

        // Every session is named after its block (Gap 114, D1) — the block itself stays quiet
        // about this moment (its Started toast already fired when it activated).
        var session = await _sessions.GetByIdAsync(domainEvent.SessionId, cancellationToken);
        var block = session is null
            ? null
            : await _blocks.GetByIdAsync(session.OriginatingBlockId, cancellationToken) as FocusBlock;
        var preset = block is null ? null : await _presets.GetByIdAsync(block.BlockPresetId, cancellationToken);
        var blockName = block?.DisplayName(preset?.Name ?? "");

        await _notifier.NotifyAsync(new SessionStarted(block?.BookingType ?? FocusBlockBookingType.Spontaneous, blockName), cancellationToken);
    }
}
