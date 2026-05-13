using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Domain.Services;

namespace NenFocus.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // 1. Register MediatR (or your custom Handlers)
        // services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));

        // 2. Register FluentValidation (Scans all folders for Validators)
        services.AddValidatorsFromAssembly(assembly);

        // 3. Register Domain Services
        services.AddScoped<IPolicyEngine, PolicyEngine>();

        return services;
    }
}