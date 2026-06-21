using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application;
using NenFocus.Application.Interfaces;
using NenFocus.Infrastructure.Events;
using NenFocus.Infrastructure.Notifications;
using NenFocus.Infrastructure.Persistence;
using NenFocus.Infrastructure.Persistence.Repositories;
using NenFocus.Infrastructure.Services;
using System.Reflection;

namespace NenFocus.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string dbPath)
    {
        // -- Application layer --
        services.AddApplication();
        RegisterDecoratedCommandHandlers(services);

        // -- Database --
        // 1. Register the Factory (This satisfies BlockedAttemptTracker)
        services.AddDbContextFactory<NenFocusDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // 2. Register the Context itself (This satisfies the repositories)
        services.AddDbContext<NenFocusDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // -- Repositories --
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IBlockProfileRepository, BlockProfileRepository>();
        services.AddScoped<IPlannedBlockRepository, PlannedBlockRepository>();


        // -- Notifications --
        services.AddSingleton<INotificationService, WindowsNotificationService>();

        // -- Enforcement --
        services.AddSingleton<IBlockedAttemptTracker, BlockedAttemptTracker>();
        services.AddSingleton<IEnforcementService, WindowsEnforcementService>();

        // -- Session state (UI noticeboard) --
        services.AddSingleton<ISessionStateService, SessionStateService>();

        // -- Session countdown monitor --
        services.AddSingleton<IActiveSessionMonitor, ActiveSessionMonitor>();

        // -- Planner clock (current-time indicator) --
        services.AddSingleton<IPlannerClockService, PlannerClockService>();

        // -- Domain event publisher --
        services.AddSingleton<IDomainEventPublisher, InProcessDomainEventPublisher>();

        // -- Unit of Work --
        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();

        // -- Scheduled enforcement background service --
        services.AddHostedService<ScheduledEnforcementService>();

        return services;
    }

    private static void RegisterDecoratedCommandHandlers(IServiceCollection services)
    {
        var appAssembly = Assembly.GetAssembly(typeof(NenFocus.Application.DependencyInjection))!;
        var commandHandlerInterface = typeof(ICommandHandler<,>);
        var decoratorType = typeof(TransactionalCommandHandler<,>);

        var handlers = appAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType &&
                            i.GetGenericTypeDefinition() == commandHandlerInterface)
                .Select(i => (ConcreteType: t, Interface: i)))
            .ToList();

        foreach (var (concreteType, @interface) in handlers)
        {
            services.AddTransient(concreteType);

            var typeArgs = @interface.GetGenericArguments();
            var closedDecorator = decoratorType.MakeGenericType(typeArgs);

            services.AddTransient(@interface, sp =>
            {
                var inner = sp.GetRequiredService(concreteType);
                var uow = sp.GetRequiredService<IUnitOfWork>();
                return Activator.CreateInstance(closedDecorator, inner, uow)!;
            });
        }
    }
}