using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application;
using NenFocus.Application.Interfaces;
using NenFocus.Infrastructure.Events;
using NenFocus.Infrastructure.Notifications;
using NenFocus.Infrastructure.Persistence;
using NenFocus.Infrastructure.Persistence.Repositories;
using NenFocus.Infrastructure.Services;

namespace NenFocus.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string dbPath)
    {
        // ── Application layer ──────────────────────────────────────────────
        services.AddApplication();

        // ── Database ───────────────────────────────────────────────────────
        // 1. Register the Factory (This satisfies BlockedAttemptTracker)
        services.AddDbContextFactory<NenFocusDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // 2. Register the Context itself (This satisfies the repositories)
        services.AddDbContext<NenFocusDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // ── Repositories ───────────────────────────────────────────────────
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IBlockProfileRepository, BlockProfileRepository>();
        services.AddScoped<IPlannedBlockRepository, PlannedBlockRepository>();


        // ── Notifications ──────────────────────────────────────────────────
        services.AddSingleton<INotificationService, WindowsNotificationService>();

        // ── Enforcement ────────────────────────────────────────────────────
        services.AddSingleton<IBlockedAttemptTracker, BlockedAttemptTracker>();
        services.AddSingleton<IEnforcementService, WindowsEnforcementService>();

        // ── Session state (UI noticeboard) ─────────────────────────────────
        services.AddSingleton<ISessionStateService, SessionStateService>();

        // ── Session countdown monitor ──────────────────────────────────────
        services.AddSingleton<IActiveSessionMonitor, ActiveSessionMonitor>();

        // ── Domain event publisher ─────────────────────────────────────────
        services.AddSingleton<IDomainEventPublisher, InProcessDomainEventPublisher>();

        // ── Scheduled enforcement background service ───────────────────────
        services.AddHostedService<ScheduledEnforcementService>();

        return services;
    }
}