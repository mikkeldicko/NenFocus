using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetSessionHistory;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetSessionHistoryQueryHandlerTests
{
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();

    private GetSessionHistoryQueryHandler CreateHandler() => new(_sessions, _profiles);

    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 1, 31);

    [Fact]
    public async Task HandleAsync_PassesDateRangeAndProfileIdToRepository()
    {
        var profileId = Guid.NewGuid();
        _sessions.GetHistoryAsync(From, To, profileId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Session>)[]);
        _profiles.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockProfile>)[]);

        await CreateHandler().HandleAsync(new GetSessionHistoryQuery { From = From, To = To, ProfileId = profileId });

        await _sessions.Received(1).GetHistoryAsync(From, To, profileId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_CompletedSession_ComputesActualDurationFromStartAndEnd()
    {
        var profileId = Guid.NewGuid();
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(60)),
            profileId, SessionOrigin.Spontaneous);
        var startedAt = new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);
        session.Start(startedAt);
        session.Complete(startedAt.AddMinutes(45));
        _sessions.GetHistoryAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[session]);
        _profiles.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
            (IReadOnlyList<BlockProfile>)[BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, startedAt)]);

        var result = await CreateHandler().HandleAsync(new GetSessionHistoryQuery { From = From, To = To });

        var dto = result.Should().ContainSingle().Subject;
        dto.ProfileName.Should().Be("Deep Work");
        dto.PlannedDurationMinutes.Should().Be(60);
        dto.ActualDurationMinutes.Should().Be(45);
        dto.StartedAt.Should().Be(startedAt);
    }

    [Fact]
    public async Task HandleAsync_ProfileNotFound_ReturnsUnknownProfileName()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(DateTime.UtcNow.AddHours(-1));
        session.Complete(DateTime.UtcNow);
        _sessions.GetHistoryAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[session]);
        _profiles.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockProfile>)[]);

        var result = await CreateHandler().HandleAsync(new GetSessionHistoryQuery { From = From, To = To });

        result.Should().ContainSingle().Subject.ProfileName.Should().Be("Unknown");
    }

    [Fact]
    public async Task HandleAsync_MissedSession_ActualDurationIsZero()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("(missed — no goal set)"), new SessionDuration(TimeSpan.FromMinutes(60)),
            Guid.NewGuid(), SessionOrigin.Planned, originatingBlockId: Guid.NewGuid());
        var scheduledStart = new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);
        session.Miss(scheduledStart, scheduledStart.AddHours(1));
        _sessions.GetHistoryAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[session]);
        _profiles.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockProfile>)[]);

        var result = await CreateHandler().HandleAsync(new GetSessionHistoryQuery { From = From, To = To });

        var dto = result.Should().ContainSingle().Subject;
        dto.ActualDurationMinutes.Should().Be(0);
        dto.StartedAt.Should().Be(scheduledStart);
    }

    [Fact]
    public async Task HandleAsync_SessionNeverStarted_ActualDurationIsZero()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        _sessions.GetHistoryAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[session]);
        _profiles.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockProfile>)[]);

        var result = await CreateHandler().HandleAsync(new GetSessionHistoryQuery { From = From, To = To });

        result.Should().ContainSingle().Subject.ActualDurationMinutes.Should().Be(0);
    }
}
