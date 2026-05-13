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
        // Find all non-abstract classes that implement ICommandHandler or IQueryHandler
        var handlerTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces(), (implementation, @interface) => new { implementation, @interface })
            .Where(x => x.@interface.IsGenericType &&
                        (x.@interface.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) ||
                         x.@interface.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)));

        foreach (var handler in handlerTypes)
        {
            // Register as Transient so a new handler is created for each request
            services.AddTransient(handler.@interface, handler.implementation);
        }
    }
}