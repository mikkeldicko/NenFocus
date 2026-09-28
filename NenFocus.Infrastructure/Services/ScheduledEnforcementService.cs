using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetFocusBlockAwaitingRitual;
using NenFocus.Application.UseCases.Sessions.SweepMissedFocusBlocks;

namespace NenFocus.Infrastructure.Services;

/// <summary>
/// The clock that drives time-based behaviour: every tick it sweeps missed blocks, syncs
/// enforcement, reports block markers passed (starting soon, started, ended), and
/// answers whether the Ritual is needed. It
/// holds no rule of its own — each question is answered by the module that owns it (Gap 62).
/// </summary>
public sealed class ScheduledEnforcementService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISessionStateService _sessionState;
    private readonly IEnforcementSynchronizer _synchronizer;
    private readonly ISystemClock _clock;
    private readonly ILogger<ScheduledEnforcementService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(5);

    // The end of the last tick whose block markers were reported — the heartbeat's only
    // memory, like ActiveSessionMonitor's break cursor. Null until the first tick, which
    // reports nothing: a marker passed before the app was watching is not announced late.
    private DateTime? _lastBoundaryCheckAt;

    public ScheduledEnforcementService(
        IServiceScopeFactory scopeFactory,
        ISessionStateService sessionState,
        IEnforcementSynchronizer synchronizer,
        ISystemClock clock,
        ILogger<ScheduledEnforcementService> logger)
    {
        _scopeFactory = scopeFactory;
        _sessionState = sessionState;
        _synchronizer = synchronizer;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var now = _clock.UtcNow;

                // Backstop for the case where this heartbeat's own edge-triggered ritual-
                // required tracking never observed a block go active — e.g. the system slept
                // through the block's entire window, or (in dev) the debug clock jumped past
                // it in one step. SweepMissedFocusBlocksCommandHandler is the same level-based
                // "block's End has passed with no ritual" check SessionStateService.
                // InitializeAsync already runs once at startup; running it every tick too means
                // a block can never sit unresolved for longer than one heartbeat interval.
                // Unlike the startup sweep, anything this one finds was never announced to
                // anyone live — NotifyPostSessionsReady is what lets ShellViewModel surface it
                // as a Post-Session popup immediately instead of only at next app restart.
                //
                // AsOf = now (the same value passed to SyncAsync and GetFocusBlockAwaitingRitual
                // below) is deliberate, not incidental: without it, the sweep would read its own
                // fresh ISystemClock.UtcNow, which can drift from `now` by however long DI/DB/
                // debugger overhead takes between the two reads. A block whose End falls in
                // that gap could then get two different verdicts in the same tick — the sweep
                // (later clock) marks it Missed while the Ritual check (earlier `now`) still
                // sees it inside its active window and fires SetRitualRequired for the same
                // block, sending the user into a Ritual for a block that already has a Missed
                // session. One "now" per tick closes that race entirely.
                var sweepHandler = scope.ServiceProvider
                    .GetRequiredService<ICommandHandler<SweepMissedFocusBlocksCommand, SweepMissedFocusBlocksResult>>();
                var sweepResult = await sweepHandler.HandleAsync(new SweepMissedFocusBlocksCommand { AsOf = now }, stoppingToken);
                _sessionState.NotifyPostSessionsReady(sweepResult.MissedSessions);

                await _synchronizer.SyncAsync(now, stoppingToken);

                // After the sync, so "Blocking is on/off" is already true when the user reads it.
                var blockMarkers = scope.ServiceProvider.GetRequiredService<IBlockMarkerNotifier>();
                await blockMarkers.NotifyPassedAsync(_lastBoundaryCheckAt ?? now, now, stoppingToken);
                _lastBoundaryCheckAt = now;

                var ritualHandler = scope.ServiceProvider
                    .GetRequiredService<IQueryHandler<GetFocusBlockAwaitingRitualQuery, Guid?>>();
                var awaitingRitualBlockId = await ritualHandler.HandleAsync(
                    new GetFocusBlockAwaitingRitualQuery(now), stoppingToken);

                if (awaitingRitualBlockId is { } blockId)
                    _sessionState.SetRitualRequired(true, blockId);
                else
                    _sessionState.SetRitualRequired(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Enforcement heartbeat failed");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
