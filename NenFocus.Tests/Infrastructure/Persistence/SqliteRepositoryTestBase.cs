using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NenFocus.Infrastructure.Persistence;

namespace NenFocus.Tests.Infrastructure.Persistence;

/// <summary>
/// Base class for repository tests. Each test gets a fresh in-memory SQLite
/// database (schema created from the real EF Core model, including seed data)
/// instead of a mocked DbContext, per TESTING_GUIDE.md's rule for repositories.
/// </summary>
public abstract class SqliteRepositoryTestBase : IDisposable
{
    private readonly SqliteConnection _connection;
    protected readonly NenFocusDbContext Context;

    protected SqliteRepositoryTestBase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<NenFocusDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new NenFocusDbContext(options);
        Context.Database.EnsureCreated();
    }

    /// <summary>A second context on the same database — reads through it hit SQLite, not
    /// the first context's change tracker, so a save-then-load is a real round trip.</summary>
    protected NenFocusDbContext CreateFreshContext() =>
        new(new DbContextOptionsBuilder<NenFocusDbContext>().UseSqlite(_connection).Options);

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
