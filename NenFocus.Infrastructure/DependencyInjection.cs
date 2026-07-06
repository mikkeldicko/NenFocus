using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application;
using NenFocus.Application.Interfaces;
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
        services.AddApplication();
        RegisterDecoratedCommandHandlers(services);

        // Both factory and context are needed: factory for singleton services that
        // create their own isolated contexts; context for scoped repositories.
        services.AddDbContextFactory<NenFocusDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        services.AddDbContext<NenFocusDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IBlockPresetRepository, BlockPresetRepository>();
        services.AddScoped<IPlannedBlockRepository, PlannedBlockRepository>();

        services.AddSingleton<INotificationService, WindowsNotificationService>();

        services.AddSingleton<IBlockedAttemptTracker, BlockedAttemptTracker>();
        services.AddSingleton<IEnforcementService, WindowsEnforcementService>();

        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<ISessionStateService, SessionStateService>();
        services.AddSingleton<IActiveSessionMonitor, ActiveSessionMonitor>();
        services.AddSingleton<IPlannerClockService, PlannerClockService>();
        services.AddSingleton<IDomainEventPublisher, InProcessDomainEventPublisher>();

        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();

        services.AddHostedService<ScheduledEnforcementService>();

        return services;
    }

    private static void RegisterDecoratedCommandHandlers(IServiceCollection services)
    {
        var appAssembly = Assembly.GetAssembly(typeof(NenFocus.Application.DependencyInjection))!;
        var commandHandlerInterface = typeof(ICommandHandler<,>);
        var transactionalDecorator = typeof(TransactionalCommandHandler<,>);
        var validatingDecorator = typeof(ValidatingCommandHandler<,>);
        var commandResultInterface = typeof(ICommandResult<>);

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

            var typeArgs = @interface.GetGenericArguments(); // [TCommand, TResult]
            var resultType = typeArgs[1];

            var closedTransactional = transactionalDecorator.MakeGenericType(typeArgs);

            // Only wrap with ValidatingCommandHandler when the result type
            // implements ICommandResult<TSelf> (i.e. can express validation failure).
            bool supportsValidation = resultType.GetInterfaces().Any(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition() == commandResultInterface &&
                i.GetGenericArguments()[0] == resultType);

            services.AddTransient(@interface, sp =>
            {
                var inner = sp.GetRequiredService(concreteType);
                var uow = sp.GetRequiredService<IUnitOfWork>();
                var transactional = Activator.CreateInstance(closedTransactional, inner, uow)!;

                if (!supportsValidation) return transactional;

                var closedValidating = validatingDecorator.MakeGenericType(typeArgs);
                return Activator.CreateInstance(closedValidating, transactional, sp)!;
            });
        }
    }
}
