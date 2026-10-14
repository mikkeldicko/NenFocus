using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Services;
using NenFocus.Domain.Services;

namespace NenFocus.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        RegisterHandlers(services, assembly);

        services.AddValidatorsFromAssembly(assembly);

        services.AddScoped<PolicyEngine>();
        services.AddScoped<BlockPlacementChecker>();
        services.AddScoped<BlockChangeChecker>();

        services.AddScoped<ContractBlockBatchPreparer>();

        // Singleton: its lock must be shared by every caller (the heartbeat and every event
        // handler) to stop a stale sync overwriting a fresh one. Safe as a singleton because
        // it reaches the database only through IQueryDispatcher, which opens a fresh scope
        // per query (Gap 40), and IEnforcementService is itself a singleton.
        services.AddSingleton<IEnforcementSynchronizer, EnforcementSynchronizer>();

        // Singleton: stateless, and called by ActiveSessionMonitor (a singleton); its only
        // dependencies are singletons too (Gap 63).
        services.AddSingleton<IBreakTransitionResponder, BreakTransitionResponder>();

        // Singleton: pure wording over the (singleton) toaster; the one door to it.
        services.AddSingleton<INotifier, Notifier>();

        // Scoped: reads IPlannedBlockRepository, so the heartbeat resolves it from its own
        // per-tick scope rather than holding one DbContext for the app's lifetime (Gap 40).
        services.AddScoped<IBlockMarkerNotifier, BlockMarkerNotifier>();

        // Singletons: app-wide state the UI observes. They reach the database only through
        // IQueryDispatcher, a fresh scope per query (Gap 40). Moved from Infrastructure (Gap 112).
        services.AddSingleton<IActiveSessionState, ActiveSessionState>();
        services.AddSingleton<IRitualPrompt, RitualPrompt>();
        services.AddSingleton<IRecapRelay, RecapRelay>();

        return services;
    }

    private static void RegisterHandlers(IServiceCollection services, Assembly assembly)
    {
        var implementations = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false });

        foreach (var type in implementations)
        {
            var interfaces = type.GetInterfaces().Where(i => i.IsGenericType);
            foreach (var @interface in interfaces)
            {
                var def = @interface.GetGenericTypeDefinition();
                // Query handlers registered here; command handlers are wrapped by
                // TransactionCommandHandlerDecorator in Infrastructure DI.
                if (def == typeof(IQueryHandler<,>))
                    services.AddTransient(@interface, type);

                // Event handlers registered by their generic interface so
                // InProcessDomainEventPublisher can resolve them by event type.
                if (def == typeof(IDomainEventHandler<>))
                    services.AddTransient(@interface, type);
            }
        }
    }
}
