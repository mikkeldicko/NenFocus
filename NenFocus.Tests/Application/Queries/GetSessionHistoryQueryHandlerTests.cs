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
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly ISystemClock _clock = TestClock.Create();

    public GetSessionHistoryQueryHandlerTests()
    {
        _clock.SetNow(DateTime.UtcNow);
    }

    private GetSessionHistoryQueryHandler CreateHandler() => new(_sessions, _presets, _clock);

    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 1, 31);

    [Fact]
    public async Task HandleAsync_PassesDateRangeAndPresetIdToRepository()
    {
        var presetId = Guid.NewGuid();
        _sessions.GetHistoryAsync(From, To, presetId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Session>)[]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        await CreateHandler().HandleAsync(new GetSessionHistoryQuery { From = From, To = To, PresetId = presetId });

        await _sessions.Received(1).GetHistoryAsync(From, To, presetId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_CompletedSession_ComputesActualDurationFromStartAndEnd()
    {
        var presetId = Guid.NewGuid();
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(60)),
            presetId, SessionOrigin.Spontaneous);
        var startedAt = new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);
        session.Start(At.Utc(startedAt));
        session.Complete(At.Utc(startedAt.AddMinutes(45)));
        _sessions.GetHistoryAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[session]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
            (IReadOnlyList<BlockPreset>)[BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(startedAt))]);

        var result = await CreateHandler().HandleAsync(new GetSessionHistoryQuery { From = From, To = To });

        var dto = result.Should().ContainSingle().Subject;
        dto.PresetName.Should().Be("Deep Work");
        dto.PlannedDurationMinutes.Should().Be(60);
        dto.ActualDurationMinutes.Should().Be(45);
        dto.StartedAt.Should().Be(At.Utc(startedAt));
    }

    [Fact]
    public async Task HandleAsync_PresetNotFound_ReturnsUnknownPresetName()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(DateTime.UtcNow.AddHours(-1)));
        session.Complete(At.Utc(DateTime.UtcNow));
        _sessions.GetHistoryAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[session]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetSessionHistoryQuery { From = From, To = To });

        result.Should().ContainSingle().Subject.PresetName.Should().Be("Unknown");
    }

    [Fact]
    public async Task HandleAsync_MissedSession_ActualDurationIsZero()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("(missed — no goal set)"), new SessionDuration(TimeSpan.FromMinutes(60)),
            Guid.NewGuid(), SessionOrigin.Planned, originatingBlockId: Guid.NewGuid());
        var scheduledStart = new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);
        session.Miss(new TimeSlot(At.Utc(scheduledStart), At.Utc(scheduledStart.AddHours(1))));
        _sessions.GetHistoryAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[session]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetSessionHistoryQuery { From = From, To = To });

        var dto = result.Should().ContainSingle().Subject;
        dto.ActualDurationMinutes.Should().Be(0);
        dto.StartedAt.Should().Be(At.Utc(scheduledStart));
    }

    [Fact]
    public async Task HandleAsync_SessionNeverStarted_ActualDurationIsZero()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        _sessions.GetHistoryAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[session]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetSessionHistoryQuery { From = From, To = To });

        result.Should().ContainSingle().Subject.ActualDurationMinutes.Should().Be(0);
    }
}
