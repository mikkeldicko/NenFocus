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

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
