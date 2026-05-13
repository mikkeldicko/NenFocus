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
            // 1. Register Command/Query Handlers (Generics)
            var interfaces = type.GetInterfaces().Where(i => i.IsGenericType);
            foreach (var @interface in interfaces)
            {
                var def = @interface.GetGenericTypeDefinition();
                if (def == typeof(ICommandHandler<,>) || def == typeof(IQueryHandler<,>))
                {
                    services.AddTransient(@interface, type);
                }
            }

            // 2. Register Event Handlers (Concrete classes)
            // This fixes the "No service for type SessionStartedEventHandler" error
            if (type.Name.EndsWith("EventHandler"))
            {
                services.AddTransient(type);
            }
        }
    }
}
