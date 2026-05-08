using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NenFocus.Infrastructure.Persistence;

namespace NenFocus.Infrastructure;

public static class HostExtensions
{
    /// <summary>
    /// Ensures the SQLite database exists and all migrations are applied.
    /// Call this once at app startup before the UI launches.
    /// </summary>
    public static IHost EnsureDatabase(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NenFocusDbContext>();
        context.Database.Migrate();
        return host;
    }
}