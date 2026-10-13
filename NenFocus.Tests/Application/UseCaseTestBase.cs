using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure;
using NenFocus.Infrastructure.Persistence;
using NSubstitute;

namespace NenFocus.Tests.Application;

/// <summary>
/// Runs a use case the way the app does (Gap 110): the real composition root, every command
/// through <see cref="ICommandDispatcher"/> — validation, the transaction and the unit of work
/// included, a fresh scope per command — against a fresh in-memory SQLite database per test.
/// Only what lies outside the app is faked: the clock (<see cref="Clock"/>, UTC unless a test
/// says otherwise), OS blocking (<see cref="Enforcement"/>) and toasts (<see cref="Notifications"/>).
/// Arrange with <see cref="SeedAsync"/>, assert on what was stored with <see cref="ReadAsync{T}"/>:
/// both use their own context, so nothing is read back from a change tracker.
/// </summary>
public abstract class UseCaseTestBase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<NenFocusDbContext> _options;
    private readonly ServiceProvider _provider;

    /// <summary>
    /// Rows every fresh database starts with (NenFocusDbContext's seed data) — for a test that only
    /// needs "some valid preset". A test that checks a preset's contents (name, rhythm) stores its
    /// own with <see cref="FocusPresetAsync"/> or <see cref="BreakTimerPresetAsync"/>, so a change
    /// to the seed data can't break it.
    /// </summary>
    protected static readonly Guid SeededFocusPresetId = new("a0000001-0000-0000-0000-000000000002");
    protected static readonly Guid SeededBackgroundPresetId = new("a0000001-0000-0000-0000-000000000003");
    protected static readonly Guid SeededContractPresetId = new("c0000003-0000-0000-0000-000000000001");

    protected ISystemClock Clock { get; } = TestClock.Create();
    protected IEnforcementService Enforcement { get; } = Substitute.For<IEnforcementService>();
    protected INotificationService Notifications { get; } = Substitute.For<INotificationService>();

    protected UseCaseTestBase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<NenFocusDbContext>().UseSqlite(_connection).Options;
        using (var context = new NenFocusDbContext(_options))
            context.Database.EnsureCreated();

        Clock.SetTimeZoneOffset(TimeSpan.Zero);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(dbPath: "unused-by-use-case-tests.db");
        // Registered after AddInfrastructure, so these win: the in-memory database, our clock,
        // no hosts file, no toasts.
        services.AddSingleton<IDbContextFactory<NenFocusDbContext>>(new InMemoryContextFactory(_options));
        services.AddScoped(_ => new NenFocusDbContext(_options));
        services.AddSingleton(Clock);
        services.AddSingleton(Enforcement);
        services.AddSingleton(Notifications);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Runs one command through the real pipeline, in its own scope, as the app's dispatcher does.</summary>
    protected Task<TResult> RunAsync<TResult>(ICommand<TResult> command) =>
        _provider.GetRequiredService<ICommandDispatcher>().DispatchAsync(command);

    /// <summary>An app-wide service as the app has it — e.g. <c>IRecapRelay</c>, to see what the Shell is told.</summary>
    protected T Resolve<T>() where T : notnull => _provider.GetRequiredService<T>();

    /// <summary>Stores the given aggregates directly, as the state a test starts from.</summary>
    protected async Task SeedAsync(params object[] aggregates)
    {
        await using var context = new NenFocusDbContext(_options);
        context.AddRange(aggregates);
        await context.SaveChangesAsync();
    }

    /// <summary>Stores a Focus block preset of the test's own and returns its id.</summary>
    protected async Task<Guid> FocusPresetAsync(string name)
    {
        var preset = BlockPreset.Create(Guid.NewGuid(), name, WebsiteFilterType.Blocklist, BlockPresetType.Focus, Clock.Now);
        await SeedAsync(preset);
        return preset.Id;
    }

    /// <summary>Stores a break timer preset of the test's own and returns its id (names are unique — not a seeded one's).</summary>
    protected async Task<Guid> BreakTimerPresetAsync(string name, BreakTimer timer)
    {
        var preset = BreakTimerPreset.Create(Guid.NewGuid(), name, timer);
        await SeedAsync(preset);
        return preset.Id;
    }

    /// <summary>Reads what is stored now, through a context of its own.</summary>
    protected async Task<T> ReadAsync<T>(Func<NenFocusDbContext, Task<T>> read)
    {
        await using var context = new NenFocusDbContext(_options);
        return await read(context);
    }

    /// <summary>Reads one stored aggregate by id (owned values included), or null.</summary>
    protected Task<T?> FindAsync<T>(Guid id) where T : class =>
        ReadAsync(context => context.Set<T>().FindAsync(id).AsTask());

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private sealed class InMemoryContextFactory(DbContextOptions<NenFocusDbContext> options)
        : IDbContextFactory<NenFocusDbContext>
    {
        public NenFocusDbContext CreateDbContext() => new(options);
    }
}
