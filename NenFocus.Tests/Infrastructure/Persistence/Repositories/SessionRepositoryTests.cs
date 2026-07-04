using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Persistence.Repositories;

namespace NenFocus.Tests.Infrastructure.Persistence.Repositories;

public class SessionRepositoryTests : SqliteRepositoryTestBase
{
    private SessionRepository CreateRepository() => new(Context);

    private static Session CreateSession(Guid? profileId = null, DateTime? startedAt = null)
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            profileId ?? Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(startedAt ?? DateTime.UtcNow);
        return session;
    }

    [Fact]
    public async Task SaveAsync_NewSession_ThenGetById_Retrieves()
    {
        var repo = CreateRepository();
        var session = CreateSession();

        await repo.SaveAsync(session);
        await Context.SaveChangesAsync();

        (await CreateRepository().GetByIdAsync(session.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task SaveAsync_ExistingSession_UpdatesStatus()
    {
        var repo = CreateRepository();
        var session = CreateSession();
        await repo.SaveAsync(session);
        await Context.SaveChangesAsync();

        session.Complete(DateTime.UtcNow);
        await CreateRepository().SaveAsync(session);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(session.Id);
        reloaded!.Status.Should().Be(SessionStatus.Completed);
    }

    [Fact]
    public async Task GetActiveAsync_NoActiveSession_ReturnsNull()
    {
        var result = await CreateRepository().GetActiveAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveAsync_ActiveSessionExists_ReturnsIt()
    {
        var repo = CreateRepository();
        var session = CreateSession();
        await repo.SaveAsync(session);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetActiveAsync();

        result!.Id.Should().Be(session.Id);
    }

    [Fact]
    public async Task GetActiveAsync_CompletedSession_IsNotReturned()
    {
        var repo = CreateRepository();
        var session = CreateSession();
        session.Complete(DateTime.UtcNow);
        await repo.SaveAsync(session);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetActiveAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task HasActiveSessionAsync_ReflectsCurrentState()
    {
        var repo = CreateRepository();
        (await repo.HasActiveSessionAsync()).Should().BeFalse();

        var session = CreateSession();
        await repo.SaveAsync(session);
        await Context.SaveChangesAsync();

        (await CreateRepository().HasActiveSessionAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task GetHistoryAsync_FiltersByDateRangeOnStartedAt()
    {
        var repo = CreateRepository();
        var inRange = CreateSession(startedAt: new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc));
        var outOfRange = CreateSession(startedAt: new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc));
        await repo.SaveAsync(inRange);
        await repo.SaveAsync(outOfRange);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetHistoryAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        result.Should().ContainSingle(s => s.Id == inRange.Id);
    }

    [Fact]
    public async Task GetHistoryAsync_FiltersByProfileIdWhenProvided()
    {
        var profileId = Guid.NewGuid();
        var repo = CreateRepository();
        var matching = CreateSession(profileId, new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc));
        var other = CreateSession(Guid.NewGuid(), new DateTime(2026, 1, 16, 10, 0, 0, DateTimeKind.Utc));
        await repo.SaveAsync(matching);
        await repo.SaveAsync(other);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetHistoryAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), profileId);

        result.Should().ContainSingle(s => s.Id == matching.Id);
    }

    [Fact]
    public async Task GetHistoryAsync_OrdersByStartedAtDescending()
    {
        var repo = CreateRepository();
        var earlier = CreateSession(startedAt: new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc));
        var later = CreateSession(startedAt: new DateTime(2026, 1, 20, 10, 0, 0, DateTimeKind.Utc));
        await repo.SaveAsync(earlier);
        await repo.SaveAsync(later);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetHistoryAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        result.Select(s => s.Id).Should().Equal(later.Id, earlier.Id);
    }

    [Fact]
    public async Task GetActiveSessionsAsync_ReturnsOnlyActiveOrOnBreakSessions()
    {
        var repo = CreateRepository();
        var active = CreateSession();
        var completed = CreateSession();
        completed.Complete(DateTime.UtcNow);
        await repo.SaveAsync(active);
        await repo.SaveAsync(completed);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetActiveSessionsAsync();

        result.Should().ContainSingle(s => s.Id == active.Id);
    }
}
