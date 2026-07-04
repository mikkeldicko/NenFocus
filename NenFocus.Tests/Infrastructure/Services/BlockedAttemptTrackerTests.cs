using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;
using NenFocus.Infrastructure.Persistence;
using NenFocus.Infrastructure.Services;
using NSubstitute;

namespace NenFocus.Tests.Infrastructure.Services;

public class BlockedAttemptTrackerTests : IDisposable
{
    private sealed class FakeDbContextFactory : IDbContextFactory<NenFocusDbContext>
    {
        private readonly DbContextOptions<NenFocusDbContext> _options;

        public FakeDbContextFactory(DbContextOptions<NenFocusDbContext> options) => _options = options;

        public NenFocusDbContext CreateDbContext() => new(_options);

        public Task<NenFocusDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    private readonly SqliteConnection _connection;
    private readonly FakeDbContextFactory _dbFactory;
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public BlockedAttemptTrackerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<NenFocusDbContext>().UseSqlite(_connection).Options;
        _dbFactory = new FakeDbContextFactory(options);
        using var context = _dbFactory.CreateDbContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private BlockedAttemptTracker CreateTracker() => new(_dbFactory, _clock);

    [Fact]
    public async Task RecordAppBlockedAsync_PersistsAttempt()
    {
        var sessionId = Guid.NewGuid();
        _clock.UtcNow.Returns(DateTime.UtcNow);

        await CreateTracker().RecordAppBlockedAsync(sessionId, "chrome");

        var attempts = await CreateTracker().GetAttemptsForSessionAsync(sessionId);
        attempts.Should().ContainSingle(a => a.ResourceName == "chrome" && a.AttemptType == BlockedAttemptType.App);
    }

    [Fact]
    public async Task RecordWebBlockedAsync_PersistsAttempt()
    {
        var sessionId = Guid.NewGuid();
        _clock.UtcNow.Returns(DateTime.UtcNow);

        await CreateTracker().RecordWebBlockedAsync(sessionId, "facebook.com");

        var attempts = await CreateTracker().GetAttemptsForSessionAsync(sessionId);
        attempts.Should().ContainSingle(a => a.ResourceName == "facebook.com" && a.AttemptType == BlockedAttemptType.Website);
    }

    [Fact]
    public async Task RecordAppBlockedAsync_CalledTwiceWithinTwoSeconds_OnlyPersistsOnce()
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        _clock.UtcNow.Returns(now, now.AddSeconds(1));

        await CreateTracker().RecordAppBlockedAsync(sessionId, "chrome");
        await CreateTracker().RecordAppBlockedAsync(sessionId, "chrome");

        var attempts = await CreateTracker().GetAttemptsForSessionAsync(sessionId);
        attempts.Should().ContainSingle();
    }

    [Fact]
    public async Task RecordAppBlockedAsync_ConcurrentCallsWithinDebounceWindow_OnlyPersistsOnce()
    {
        // Regression test for the check-then-insert race: the kill loop and the
        // extension's /report endpoint can call this concurrently in-process. Both
        // calls must go through the same tracker instance (as they would via the
        // singleton registration in DI) for the internal SemaphoreSlim to serialize them.
        var sessionId = Guid.NewGuid();
        _clock.UtcNow.Returns(DateTime.UtcNow);
        var tracker = CreateTracker();

        await Task.WhenAll(
            tracker.RecordAppBlockedAsync(sessionId, "chrome"),
            tracker.RecordAppBlockedAsync(sessionId, "chrome"));

        var attempts = await tracker.GetAttemptsForSessionAsync(sessionId);
        attempts.Should().ContainSingle();
    }

    [Fact]
    public async Task RecordAppBlockedAsync_CalledMoreThanTwoSecondsApart_PersistsBoth()
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        _clock.UtcNow.Returns(now, now.AddSeconds(3));

        await CreateTracker().RecordAppBlockedAsync(sessionId, "chrome");
        await CreateTracker().RecordAppBlockedAsync(sessionId, "chrome");

        var attempts = await CreateTracker().GetAttemptsForSessionAsync(sessionId);
        attempts.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAttemptsForSessionAsync_OnlyReturnsMatchingSessionOrderedDescending()
    {
        var sessionId = Guid.NewGuid();
        var otherSessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        _clock.UtcNow.Returns(now, now.AddSeconds(10), now.AddSeconds(20));

        await CreateTracker().RecordAppBlockedAsync(sessionId, "chrome");
        await CreateTracker().RecordAppBlockedAsync(otherSessionId, "spotify");
        await CreateTracker().RecordWebBlockedAsync(sessionId, "facebook.com");

        var attempts = await CreateTracker().GetAttemptsForSessionAsync(sessionId);

        attempts.Should().HaveCount(2);
        attempts.Select(a => a.ResourceName).Should().Equal("facebook.com", "chrome");
    }
}
