using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.SweepNenContractLifecycle;

namespace NenFocus.Infrastructure.Services;

/// <summary>
/// Periodically dispatches SweepNenContractLifecycleCommand so Scheduled contracts
/// activate and Active contracts complete promptly at their start/end moment, even while
/// the app runs continuously. Unlike SweepMissedFocusBlocksCommand
/// (dispatched once by app startup, Program.Main), a start/end
/// crossing can happen at any point during a long-running session, not just at launch —
/// a startup-only sweep would leave a due contract stuck Scheduled/Active for however long
/// the app happens to stay open.
/// </summary>
public sealed class NenContractHeartbeatService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NenContractHeartbeatService> _logger;

    // Same 5-second interval as ScheduledEnforcementService: a contract activates and
    // completes at exact local moments (StartsAt/EndsAt, Gap 65), so it must turn Active
    // alongside the Focus block that starts with it, not up to a minute later. Cheap — a
    // tick reads the handful of in-effect contracts and writes only when one is due.
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(5);

    public NenContractHeartbeatService(
        IServiceScopeFactory scopeFactory,
        ILogger<NenContractHeartbeatService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sweepHandler = scope.ServiceProvider
                    .GetRequiredService<ICommandHandler<SweepNenContractLifecycleCommand, SweepNenContractLifecycleResult>>();

                // A contract it completes owes its recap: NenContractEndedEventHandler shows it once
                // the sweep is saved (Gap 110) — the same path as a contract the user ends.
                await sweepHandler.HandleAsync(new SweepNenContractLifecycleCommand(), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Nen Contract lifecycle heartbeat failed");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
