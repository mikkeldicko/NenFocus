using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.SweepNenContractLifecycle;

namespace NenFocus.Infrastructure.Services;

/// <summary>
/// Periodically dispatches SweepNenContractLifecycleCommand so Scheduled contracts
/// activate and Active contracts complete promptly after their date boundary, even while
/// the app runs continuously across midnight. Unlike SweepMissedFocusBlocksCommand
/// (dispatched once, at startup, via SessionStateService.InitializeAsync), a day-boundary
/// crossing can happen at any point during a long-running session, not just at launch —
/// a startup-only sweep would leave a due contract stuck Scheduled/Active for however long
/// the app happens to stay open.
/// </summary>
public sealed class NenContractHeartbeatService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NenContractHeartbeatService> _logger;

    // Coarser than ScheduledEnforcementService's 5-second interval by design, not
    // oversight — this only cares about calendar-day boundaries, not session-level
    // timing, so a much lower check frequency is correct, not just tolerated.
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

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
