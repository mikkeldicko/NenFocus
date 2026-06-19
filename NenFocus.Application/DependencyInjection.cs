using System.Linq;
using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces; 
using NenFocus.Domain.Services;

namespace NenFocus.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // 1. Register All Command and Query Handlers via Reflection
        RegisterHandlers(services, assembly);

        // 2. Register FluentValidation
        services.AddValidatorsFromAssembly(assembly);

        // 3. Register Domain Services
        services.AddScoped<IPolicyEngine, PolicyEngine>();

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
                // TransactionalCommandHandler in Infrastructure DI.
                if (def == typeof(IQueryHandler<,>))
                    services.AddTransient(@interface, type);
            }

            if (type.Name.EndsWith("EventHandler"))
                services.AddTransient(type);
        }
    }
}
