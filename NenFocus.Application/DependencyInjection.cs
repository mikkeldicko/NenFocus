using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces;
using System.Reflection;
using FluentValidation;

namespace NenFocus.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // 1. FluentValidation Registration
        services.AddValidatorsFromAssembly(assembly);

        // 2. AUTOMATIC SCANNING (The "Pro Fix")
        // This replaces RegisterCommandHandlers and RegisterQueryHandlers
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.Where(c =>
                c.Name.EndsWith("Handler") && !c.IsAbstract)) // Find all Handlers
            .AsImplementedInterfaces() // Register them as ICommandHandler, IQueryHandler, etc.
            .WithScopedLifetime());

        // 3. EVENT HANDLERS (Manual or Scan)
        // If your event handlers don't share a common interface like IDomainEventHandler,
        // you can keep them manual or scan for classes ending in "EventHandler".
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.Where(c => c.Name.EndsWith("EventHandler")))
            .AsSelf()
            .WithScopedLifetime());

        return services;
    }
}