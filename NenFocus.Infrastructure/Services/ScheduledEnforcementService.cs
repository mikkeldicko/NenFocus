using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeActivePolicySlice;

namespace NenFocus.Infrastructure.Services;

public sealed class ScheduledEnforcementService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISessionStateService _sessionState;
    private readonly IEnforcementService _enforcement;
    private readonly INotificationService _notifications;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(5);

    private DateTime? _lastWarningSentAt;
    private Guid? _currentlyNotifiedBlockId; // Prevents notification spam

    public ScheduledEnforcementService(
      IServiceScopeFactory scopeFactory,
      ISessionStateService sessionState,
      IEnforcementService enforcement,
      INotificationService notifications)
    {
        _scopeFactory = scopeFactory;
        _sessionState = sessionState;
        _enforcement = enforcement;
        _notifications = notifications;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var computeHandler = scope.ServiceProvider.GetRequiredService<IQueryHandler<ComputeActivePolicySliceQuery, PolicySliceDto>>();
                    var now = DateTime.UtcNow;

                    // Make sure this stays so you get your notifications!
                    await CheckForUpcomingBlocksAsync(scope, now, stoppingToken);

                    var policy = await computeHandler.HandleAsync(new ComputeActivePolicySliceQuery { At = now }, stoppingToken);

                    if (policy is not null && !policy.IsEmpty)
                    {
                        System.Diagnostics.Debug.WriteLine($"[NenFocus] Applying block: {string.Join(", ", policy.BlockedSites)}");

                        await _enforcement.ApplyPolicyAsync(policy, stoppingToken);

                        if (_sessionState.ActiveSession == null)
                        {
                            _sessionState.SetRitualRequired(true);
                        }
                    }
                    else
                    {
                        if (policy == null) System.Diagnostics.Debug.WriteLine("[NenFocus] Policy is NULL");
                        else if (policy.IsEmpty) System.Diagnostics.Debug.WriteLine("[NenFocus] Policy is EMPTY");

                        _sessionState.SetRitualRequired(false);
                        if (_sessionState.ActiveSession == null)
                        {
                            await _enforcement.RemoveEnforcementAsync(stoppingToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NenFocus Service] ERROR: {ex.Message}");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task CheckForUpcomingBlocksAsync(IServiceScope scope, DateTime now, CancellationToken ct)
    {
        var blockRepo = scope.ServiceProvider.GetRequiredService<IPlannedBlockRepository>();

        // Look 5 minutes ahead
        var upcoming = await blockRepo.GetForRangeAsync(now, now.AddMinutes(5), ct);
        var nextBlock = upcoming.OrderBy(b => b.Start).FirstOrDefault();

        if (nextBlock != null)
        {
            var timeLeft = nextBlock.Start - now;

            // Only send warning if we haven't sent one for this specific day/block recently
            if (timeLeft.TotalMinutes <= 5 && timeLeft.TotalMinutes > 0 && _lastWarningSentAt?.AddMinutes(10) < now)
            {
                await _notifications.NotifyAsync(
                    "Block Starting Soon",
                    $"Your scheduled block will start in {Math.Round(timeLeft.TotalMinutes)} minutes. Save your work!",
                    ct);

                _lastWarningSentAt = now;
            }
        }
    }
}