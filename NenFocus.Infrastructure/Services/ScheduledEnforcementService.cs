using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeActivePolicySlice;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using System.Diagnostics;

namespace NenFocus.Infrastructure.Services;

public sealed class ScheduledEnforcementService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISessionStateService _sessionState;
    private readonly IEnforcementService _enforcement;
    private readonly INotificationService _notifications;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(5);

    private DateTime? _lastWarningSentAt;

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
                using var scope = _scopeFactory.CreateScope();

                var computeHandler = scope.ServiceProvider
                    .GetRequiredService<IQueryHandler<ComputeActivePolicySliceQuery, ActivePolicySlice>>();

                var now = DateTime.UtcNow;

                await CheckForUpcomingBlocksAsync(scope, now, stoppingToken);

                var policy = await computeHandler.HandleAsync(
                    new ComputeActivePolicySliceQuery(now),
                    stoppingToken);

                if (policy is not null && !policy.IsEmpty)
                {
                    if (policy.FocusSessionCompleted)
                    {
                        // The Focus Block is still within its scheduled window but the
                        // session has already ended — don't re-apply enforcement or
                        // push the user back into the Ritual.
                        _sessionState.SetRitualRequired(false);
                        await _enforcement.RemoveEnforcementAsync(stoppingToken);
                    }
                    else
                    {
                        await _enforcement.ApplyPolicyAsync(policy, stoppingToken);

                        if (policy.ActiveBlockType == BlockType.Focus && _sessionState.ActiveSession == null)
                            _sessionState.SetRitualRequired(true);
                        else
                            _sessionState.SetRitualRequired(false);
                    }
                }
                else
                {
                    _sessionState.SetRitualRequired(false);
                    if (_sessionState.ActiveSession == null)
                        await _enforcement.RemoveEnforcementAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NenFocus Service] ERROR: {ex.Message}");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task CheckForUpcomingBlocksAsync(IServiceScope scope, DateTime now, CancellationToken ct)
    {
        var blockRepo = scope.ServiceProvider.GetRequiredService<IPlannedBlockRepository>();
        var upcoming = await blockRepo.GetForRangeAsync(now, now.AddMinutes(5), ct);
        var nextBlock = upcoming.OrderBy(b => b.Start).FirstOrDefault();

        if (nextBlock is not null)
        {
            var timeLeft = nextBlock.Start - now;
            if (timeLeft.TotalMinutes <= 5 && timeLeft.TotalMinutes > 0 && _lastWarningSentAt?.AddMinutes(10) < now)
            {
                await _notifications.NotifyAsync("Block Starting Soon", $"Your scheduled block starts in {Math.Round(timeLeft.TotalMinutes)} minutes.", ct);
                _lastWarningSentAt = now;
            }
        }
    }
}
