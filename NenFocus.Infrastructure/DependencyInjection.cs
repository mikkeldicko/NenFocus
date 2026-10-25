using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Services;
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

        // Both a factory and a directly-injectable Scoped context are needed: factory for
        // singleton services that create their own isolated contexts; the Scoped
        // registration for repositories resolved per operation. Registering AddDbContextFactory
        // and AddDbContext independently (as this used to) silently breaks resolving
        // NenFocusDbContext from any real DI scope under ValidateScopes: true — EF Core's
        // factory-options plumbing ends up needing the root provider regardless of which
        // scope actually asked for it. The documented fix: register the factory once, then
        // derive the Scoped registration from it instead of registering both independently.

        services.AddDbContextFactory<NenFocusDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        services.AddScoped<NenFocusDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<NenFocusDbContext>>().CreateDbContext());

        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IBlockPresetRepository, BlockPresetRepository>();
        services.AddScoped<IPlannedBlockRepository, PlannedBlockRepository>();
        services.AddScoped<INenContractRepository, NenContractRepository>();
        services.AddScoped<IContractPresetRepository, ContractPresetRepository>();
        services.AddScoped<IBreakTimerPresetRepository, BreakTimerPresetRepository>();
        services.AddScoped<IBlockedAttemptRepository, BlockedAttemptRepository>();

        services.AddSingleton<INotificationService, WindowsNotificationService>();

        services.AddSingleton<IEnforcementService, WindowsEnforcementService>();

        services.AddSingleton<SystemClock>();
        services.AddSingleton<ISystemClock>(sp => sp.GetRequiredService<SystemClock>());
        services.AddSingleton<IDebugClockController>(sp => sp.GetRequiredService<SystemClock>());
        services.AddSingleton<IActiveSessionMonitor, ActiveSessionMonitor>();
        services.AddSingleton<IDisplayClock, DisplayClock>();
        services.AddSingleton<IDomainEventPublisher, InProcessDomainEventPublisher>();

        // Stateless, thread-safe — hold only IServiceScopeFactory, so one Singleton instance
        // is safe to share for the app's whole lifetime. Every individual dispatch still gets
        // its own fresh scope (and therefore its own DbContext) internally.
        services.AddSingleton<IQueryDispatcher, ScopedQueryDispatcher>();
        services.AddSingleton<ICommandDispatcher, ScopedCommandDispatcher>();

        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();

        // The one clock-driven loop (Gap 89 Slice A).
        services.AddHostedService<HeartbeatService>();

        return services;
    }

    private static void RegisterDecoratedCommandHandlers(IServiceCollection services)
    {
        var appAssembly = Assembly.GetAssembly(typeof(NenFocus.Application.DependencyInjection))!;
        var commandHandlerInterface = typeof(ICommandHandler<,>);
        var transactionalDecorator = typeof(TransactionCommandHandlerDecorator<,>);
        var validatingDecorator = typeof(ValidationCommandHandlerDecorator<,>);
        var commandResultInterface = typeof(ICommandResult<>);

        // Open generics are the decorators themselves (they live in Application too), not handlers.
        var handlers = appAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
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

            // Only wrap with ValidationCommandHandlerDecorator when the result type
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
