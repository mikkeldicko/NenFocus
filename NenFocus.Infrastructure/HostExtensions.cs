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

        // The RemoveTemporaryBlockedColumns migration (20260623123009) had an empty Up(),
        // so it never actually dropped those columns. If they already exist, mark the
        // AddTemporaryBlockedColumnsToSession migration as applied so EF skips adding them.
        SkipAddColumnsIfAlreadyExist(context, logger);

        context.Database.Migrate();

        return host;
    }

    private static void SkipAddColumnsIfAlreadyExist(NenFocusDbContext context, ILogger logger)
    {
        const string migrationId = "20260624185307_AddTemporaryBlockedColumnsToSession";

        try
        {
            var conn = context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                conn.Open();

            using var pragmaCmd = conn.CreateCommand();
            pragmaCmd.CommandText = "PRAGMA table_info(Sessions)";
            using var reader = pragmaCmd.ExecuteReader();
            var cols = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
                cols.Add(reader.GetString(1));
            reader.Close();

            if (cols.Contains("TemporaryBlockedApps") && cols.Contains("TemporaryBlockedSites"))
            {
                using var insertCmd = conn.CreateCommand();
                insertCmd.CommandText =
                    $"INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) " +
                    $"VALUES ('{migrationId}', '9.0.2')";
                insertCmd.ExecuteNonQuery();
            }
        }
        catch (SqliteException ex)
        {
            logger.LogDebug(ex, "Could not inspect Sessions table — it may not exist yet on a brand-new database");
        }
    }
}