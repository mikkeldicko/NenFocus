using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Persistence.Repositories;

namespace NenFocus.Tests.Infrastructure.Persistence.Repositories;

public class SessionRepositoryTests : SqliteRepositoryTestBase
{
    private SessionRepository CreateRepository() => new(Context);

    /// <summary>Reads through a context of its own, so they see the database, not the tracker that saved (Gap 110).</summary>
    private SessionRepository Fresh() => new(CreateFreshContext());

    private static Session CreateSession(Guid? presetId = null, DateTime? startedAt = null)
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            presetId ?? Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(startedAt ?? DateTime.UtcNow));
        return session;
    }

    /// <summary>A session that ran and ended — history. Only one session may run at a time (Gap 92).</summary>
    private static Session PastSession(Guid? presetId = null, DateTime? startedAt = null)
    {
        var session = CreateSession(presetId, startedAt);
        session.Complete(At.Utc((startedAt ?? DateTime.UtcNow).AddMinutes(30)));
        return session;
    }

    [Fact]
    public async Task Add_NewSession_ThenGetById_Retrieves()
    {
        var repo = CreateRepository();
        var session = CreateSession();

        repo.Add(session);
        await Context.SaveChangesAsync();

        (await Fresh().GetByIdAsync(session.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task CommitAfterComplete_StoresTheNewStatus()
    {
        var repo = CreateRepository();
        var session = CreateSession();
        repo.Add(session);
        await Context.SaveChangesAsync();

        session.Complete(At.Utc(DateTime.UtcNow));
        await Context.SaveChangesAsync();

        var reloaded = await Fresh().GetByIdAsync(session.Id);
        reloaded!.Status.Should().Be(SessionStatus.Completed);
    }

    [Fact]
    public async Task Add_BreakScheduleAndLabel_RoundTripThroughTheDatabase()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(120)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        var schedule = new BreakTimer(25, 5, 4, 15).ScheduleFor(TimeSpan.FromMinutes(120));
        session.Start(At.Utc(new DateTime(2026, 1, 1, 17, 0, 0, DateTimeKind.Utc)), breakSchedule: schedule, breakPresetName: "Classic");
        CreateRepository().Add(session);
        await Context.SaveChangesAsync();

        await using var fresh = CreateFreshContext();
        var reloaded = await new SessionRepository(fresh).GetByIdAsync(session.Id);

        reloaded!.BreakSchedule.Should().Be(schedule);
        reloaded.BreakPresetName.Should().Be("Classic");
    }

    [Fact]
    public async Task CommitAfterComplete_TrimsSchedule_StoredBreaksAreReplacedNotAppended()
    {
        var startedAt = new DateTime(2026, 1, 1, 17, 0, 0, DateTimeKind.Utc);
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(120)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(startedAt), breakSchedule: new BreakTimer(25, 5, 4, 15).ScheduleFor(TimeSpan.FromMinutes(120)));
        CreateRepository().Add(session);
        await Context.SaveChangesAsync();

        session.Complete(At.Utc(startedAt.AddMinutes(57))); // mid second break
        await Context.SaveChangesAsync();

        await using var fresh = CreateFreshContext();
        var reloaded = await new SessionRepository(fresh).GetByIdAsync(session.Id);
        reloaded!.BreakSchedule.Breaks.Select(b => (b.Start.TotalMinutes, b.End.TotalMinutes))
            .Should().Equal((25, 30), (55, 57));
        reloaded.BreaksTaken.Should().Be(2);
    }

    [Fact]
    public async Task GetActiveAsync_NoActiveSession_ReturnsNull()
    {
        var result = await Fresh().GetActiveAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveAsync_ActiveSessionExists_ReturnsIt()
    {
        var repo = CreateRepository();
        var session = CreateSession();
        repo.Add(session);
        await Context.SaveChangesAsync();

        var result = await Fresh().GetActiveAsync();

        result!.Id.Should().Be(session.Id);
    }

    [Fact]
    public async Task GetActiveAsync_CompletedSession_IsNotReturned()
    {
        var repo = CreateRepository();
        var session = CreateSession();
        session.Complete(At.Utc(DateTime.UtcNow));
        repo.Add(session);
        await Context.SaveChangesAsync();

        var result = await Fresh().GetActiveAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetHistoryAsync_FiltersByDateRangeOnStartedAt()
    {
        var repo = CreateRepository();
        var inRange = PastSession(startedAt: new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc));
        var outOfRange = PastSession(startedAt: new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc));
        repo.Add(inRange);
        repo.Add(outOfRange);
        await Context.SaveChangesAsync();

        var result = await Fresh().GetHistoryAsync(
            At.UtcZone.SlotCovering(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));

        result.Should().ContainSingle(s => s.Id == inRange.Id);
    }

    [Fact]
    public async Task GetHistoryAsync_AUsersDay_IsTheirLocalDay_NotTheUtcDay()
    {
        // The trap Gap 97 removed: this used to read its dates as UTC days. In Copenhagen
        // (UTC+1 in January) 15 January runs from 14 Jan 23:00 UTC to 15 Jan 23:00 UTC.
        var copenhagen = new UserTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen"));
        var repo = CreateRepository();
        var justAfterLocalMidnight = PastSession(startedAt: new DateTime(2026, 1, 14, 23, 30, 0, DateTimeKind.Utc)); // 15 Jan 00:30 local
        var lateOnTheUtcDay = PastSession(startedAt: new DateTime(2026, 1, 15, 23, 30, 0, DateTimeKind.Utc));       // 16 Jan 00:30 local
        repo.Add(justAfterLocalMidnight);
        repo.Add(lateOnTheUtcDay);
        await Context.SaveChangesAsync();

        var result = await Fresh().GetHistoryAsync(copenhagen.SlotCovering(new DateOnly(2026, 1, 15)));

        result.Should().ContainSingle(s => s.Id == justAfterLocalMidnight.Id);
    }

    [Fact]
    public async Task GetHistoryAsync_IsHalfOpen_AndLeavesOutASessionThatNeverStarted()
    {
        var slot = At.Slot(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc));
        var repo = CreateRepository();
        var atStart = PastSession(startedAt: new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));
        var atEnd = PastSession(startedAt: new DateTime(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc));
        var neverStarted = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)), Guid.NewGuid(), SessionOrigin.Spontaneous);
        repo.Add(atStart);
        repo.Add(atEnd);
        repo.Add(neverStarted);
        await Context.SaveChangesAsync();

        var result = await Fresh().GetHistoryAsync(slot);

        result.Should().ContainSingle(s => s.Id == atStart.Id);
    }

    [Fact]
    public async Task GetHistoryAsync_FiltersByPresetIdWhenProvided()
    {
        var presetId = Guid.NewGuid();
        var repo = CreateRepository();
        var matching = PastSession(presetId, new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc));
        var other = PastSession(Guid.NewGuid(), new DateTime(2026, 1, 16, 10, 0, 0, DateTimeKind.Utc));
        repo.Add(matching);
        repo.Add(other);
        await Context.SaveChangesAsync();

        var result = await Fresh().GetHistoryAsync(
            At.UtcZone.SlotCovering(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)), presetId);

        result.Should().ContainSingle(s => s.Id == matching.Id);
    }

    [Fact]
    public async Task GetHistoryAsync_OrdersByStartedAtDescending()
    {
        var repo = CreateRepository();
        var earlier = PastSession(startedAt: new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc));
        var later = PastSession(startedAt: new DateTime(2026, 1, 20, 10, 0, 0, DateTimeKind.Utc));
        repo.Add(earlier);
        repo.Add(later);
        await Context.SaveChangesAsync();

        var result = await Fresh().GetHistoryAsync(At.UtcZone.SlotCovering(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));

        result.Select(s => s.Id).Should().Equal(later.Id, earlier.Id);
    }

    [Fact]
    public async Task GetByOriginatingBlockIdAsync_NoSessionReferencesBlock_ReturnsNull()
    {
        var result = await Fresh().GetByOriginatingBlockIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByOriginatingBlockIdAsync_MissedSessionReferencesBlock_ReturnsIt()
    {
        var blockId = Guid.NewGuid();
        var repo = CreateRepository();
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("(missed — no goal set)"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Planned, originatingBlockId: blockId);
        var scheduledStart = DateTime.UtcNow.AddHours(-1);
        session.Miss(new TimeSlot(At.Utc(scheduledStart), At.Utc(scheduledStart.AddMinutes(30))));
        repo.Add(session);
        await Context.SaveChangesAsync();

        var result = await Fresh().GetByOriginatingBlockIdAsync(blockId);

        result!.Id.Should().Be(session.Id);
    }

    // ── Storage backstops (Gap 92, C+ and A8b): the Domain decides, the database refuses what slips past ──

    [Fact]
    public async Task TwoRunningSessions_CantBeStored()
    {
        var repo = CreateRepository();
        repo.Add(CreateSession());
        repo.Add(CreateSession());

        var act = () => Context.SaveChangesAsync();

        await act.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>();
    }

    [Fact]
    public async Task TwoSessionsForOneFocusBlock_CantBeStored()
    {
        var blockId = Guid.NewGuid();
        Session ForTheBlock()
        {
            var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
                Guid.NewGuid(), SessionOrigin.Planned, originatingBlockId: blockId);
            session.Miss(new TimeSlot(At.Utc(new DateTime(2026, 1, 1, 9, 0, 0)), At.Utc(new DateTime(2026, 1, 1, 9, 30, 0))));
            return session;
        }

        var repo = CreateRepository();
        repo.Add(ForTheBlock());
        repo.Add(ForTheBlock());

        var act = () => Context.SaveChangesAsync();

        await act.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>();
    }
}
