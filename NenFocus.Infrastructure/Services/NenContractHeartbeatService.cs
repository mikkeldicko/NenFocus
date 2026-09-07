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
    private readonly ISessionStateService _sessionState;
    private readonly ILogger<NenContractHeartbeatService> _logger;

    // Coarser than ScheduledEnforcementService's 5-second interval by design, not
    // oversight — this only cares about calendar-day boundaries, not session-level
    // timing, so a much lower check frequency is correct, not just tolerated.
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    public NenContractHeartbeatService(
        IServiceScopeFactory scopeFactory,
        ISessionStateService sessionState,
        ILogger<NenContractHeartbeatService> logger)
    {
        _scopeFactory = scopeFactory;
        _sessionState = sessionState;
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
                    // ISessionStateService (PostContractsReady mirrors PostSessionsReady).
                    var recapHandler = scope.ServiceProvider
                        .GetRequiredService<IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?>>();

                    var recaps = new List<NenContractRecapDto>();
                    foreach (var contractId in sweepResult.CompletedContractIds)
                    {
                        var recap = await recapHandler.HandleAsync(new GetNenContractRecapQuery(contractId), stoppingToken);
                        if (recap is not null) recaps.Add(recap);
                    }

                    _sessionState.NotifyPostContractsReady(recaps);
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
