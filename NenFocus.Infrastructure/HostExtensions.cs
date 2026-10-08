using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NenFocusDbContext>>();

        // Clear any stale migration lock left by a previously killed process.
        try { context.Database.ExecuteSqlRaw("DELETE FROM __EFMigrationsLock"); }
        catch (SqliteException ex) { logger.LogDebug(ex, "Could not clear migration lock — table may not exist yet on first run"); }

        context.Database.Migrate();

        return host;
    }
}