using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractRecap;
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
    private readonly IRecapRelay _recaps;
    private readonly ILogger<NenContractHeartbeatService> _logger;

    // Same 5-second interval as ScheduledEnforcementService: a contract activates and
    // completes at exact local moments (StartsAt/EndsAt, Gap 65), so it must turn Active
    // alongside the Focus block that starts with it, not up to a minute later. Cheap — a
    // tick reads the handful of in-effect contracts and writes only when one is due.
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(5);

    public NenContractHeartbeatService(
        IServiceScopeFactory scopeFactory,
        IRecapRelay recaps,
        ILogger<NenContractHeartbeatService> logger)
    {
        _scopeFactory = scopeFactory;
        _recaps = recaps;
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

                var sweepResult = await sweepHandler.HandleAsync(new SweepNenContractLifecycleCommand(), stoppingToken);

                if (sweepResult.CompletedContractIds.Count > 0)
                {
                    // Nobody was watching this live — the same "background process produced
                    // a fact nobody asked for yet" shape as SweepMissedFocusBlocksCommand's
                    // missed sessions, surfaced via the same kind of event on
                    // IRecapRelay (PostContractsReady mirrors PostSessionsReady).
                    var recapHandler = scope.ServiceProvider
                        .GetRequiredService<IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?>>();

                    var recaps = new List<NenContractRecapDto>();
                    foreach (var contractId in sweepResult.CompletedContractIds)
                    {
                        var recap = await recapHandler.HandleAsync(new GetNenContractRecapQuery(contractId), stoppingToken);
                        if (recap is not null) recaps.Add(recap);
                    }

                    _recaps.NotifyPostContractsReady(recaps);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Nen Contract lifecycle heartbeat failed");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
