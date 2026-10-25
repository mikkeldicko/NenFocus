using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.SweepNenContractLifecycle;
using NenFocus.Application.UseCases.Sessions.AutoEndSession;
using NenFocus.Application.UseCases.Sessions.SweepMissedFocusBlocks;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Infrastructure.Services;

/// <summary>
/// The app's one clock-driven loop (Gap 89 Slice A). Every tick takes one "now" and runs, in this
/// order: auto-end a session whose time is up, sweep missed blocks, sweep contract lifecycles, sync
/// enforcement, announce block markers passed, refresh the Ritual prompt. State-changing steps come
/// before the ones that announce or react; missed blocks before contracts matches the Shell's
/// startup order (post-sessions before recaps). The order is coordination, not a rule — what the
/// user sees first is Gap 107's. It holds no rule of its own: each step asks the module that owns
/// the answer, and every use case is dispatched (one scope per call, reading fresh after a save).
///
/// Each step fails on its own and the tick carries on — a broken contract row must never stop
/// enforcement. The first tick runs inside StartAsync, so Program.cs's awaited host start finishes
/// it before the window opens. The countdown reaching zero wakes the loop at once instead of at
/// the next beat; ticks still run one at a time, in this one loop.
/// </summary>
public sealed class HeartbeatService : BackgroundService
{
    private readonly ICommandDispatcher _commands;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRecapRelay _recaps;
    private readonly IRitualPrompt _ritualPrompt;
    private readonly IEnforcementSynchronizer _synchronizer;
    private readonly ISystemClock _clock;
    private readonly ILogger<HeartbeatService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(5);

    // Wakes the loop before the beat; holds at most one request, so a burst of "ended" readings
    // (the countdown repeats it each second until the session is gone) asks for one early tick.
    private readonly SemaphoreSlim _wake = new(0, 1);

    // The end of the last tick whose block markers were reported — the heartbeat's only
    // memory, like ActiveSessionMonitor's break cursor. Null until the first tick, which
    // reports nothing: a marker passed before the app was watching is not announced late.
    private Moment? _lastBoundaryCheckAt;

    public HeartbeatService(
        ICommandDispatcher commands,
        IServiceScopeFactory scopeFactory,
        IRecapRelay recaps,
        IRitualPrompt ritualPrompt,
        IEnforcementSynchronizer synchronizer,
        IActiveSessionMonitor monitor,
        ISystemClock clock,
        ILogger<HeartbeatService> logger)
    {
        _commands = commands;
        _scopeFactory = scopeFactory;
        _recaps = recaps;
        _ritualPrompt = ritualPrompt;
        _synchronizer = synchronizer;
        _clock = clock;
        _logger = logger;

        // The countdown's own reading says the session's time is up (the Domain's answer): end it now.
        monitor.Tick += (_, reading) => { if (reading.HasEnded) RequestTick(); };
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await TickAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _wake.WaitAsync(_interval, stoppingToken);
            await TickAsync(stoppingToken);
        }
    }

    private void RequestTick()
    {
        if (_wake.CurrentCount > 0) return;
        try { _wake.Release(); }
        catch (SemaphoreFullException) { } // another request got there first — one is enough
    }

    /// <summary>One heartbeat: every step at one "now", in the fixed order. Internal so tests can run
    /// a tick against the real composition root without waiting for the loop.</summary>
    internal async Task TickAsync(CancellationToken ct)
    {
        var now = _clock.Now;

        await RunStepAsync("auto-end session", () =>
            _commands.DispatchAsync(new AutoEndSessionCommand { AsOf = now }, ct));

        await RunStepAsync("sweep missed blocks", async () =>
        {
            var swept = await _commands.DispatchAsync(new SweepMissedFocusBlocksCommand { AsOf = now }, ct);
            // Relayed here, not by an event handler: live triage needs the sweep's whole batch at
            // once — Gap 107 moves it (exception listed in SideEffectRuleTests).
            _recaps.NotifyPostSessionsReady(swept.MissedSessions);
        });

        // A contract it completes owes its recap: NenContractEndedEventHandler shows it after the save.
        await RunStepAsync("sweep contract lifecycles", () =>
            _commands.DispatchAsync(new SweepNenContractLifecycleCommand { AsOf = now }, ct));

        await RunStepAsync("sync enforcement", () => _synchronizer.SyncAsync(now, ct));

        // After the sync, so "Blocking is on/off" is already true when the user reads it.
        await RunStepAsync("announce block markers", async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IBlockMarkerNotifier>()
                .NotifyPassedAsync(_lastBoundaryCheckAt ?? now, now, ct);
            _lastBoundaryCheckAt = now;
        });

        // Last, after the missed sweep: a block whose window just closed is already announced as
        // missed before the prompt stops naming it (Gap 92, A3, D′).
        await RunStepAsync("refresh Ritual prompt", () => _ritualPrompt.RefreshAsync(now, ct));
    }

    private async Task RunStepAsync(string step, Func<Task> run)
    {
        try
        {
            await run();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heartbeat step '{Step}' failed; the tick carries on", step);
        }
    }

    public override void Dispose()
    {
        _wake.Dispose();
        base.Dispose();
    }
}
