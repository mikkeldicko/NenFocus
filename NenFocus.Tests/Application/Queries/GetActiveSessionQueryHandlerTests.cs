using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetActiveSession;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetActiveSessionQueryHandlerTests
{
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly ISystemClock _clock = TestClock.Create();

    private GetActiveSessionQueryHandler CreateHandler() => new(_sessions, _presets, _clock);

    [Fact]
    public async Task HandleAsync_NoActiveSession_ReturnsNull()
    {
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);

        var result = await CreateHandler().HandleAsync(new GetActiveSessionQuery());

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ActiveSession_MapsPresetNameAndFields()
    {
        var presetId = Guid.NewGuid();
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Deep work"), new SessionDuration(TimeSpan.FromMinutes(45)),
            presetId, SessionOrigin.Spontaneous);
        session.Start(At.Utc(DateTime.UtcNow));
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(DateTime.UtcNow)));

        var result = await CreateHandler().HandleAsync(new GetActiveSessionQuery());

        result.Should().NotBeNull();
        result!.PresetName.Should().Be("Deep Work");
        result.Goal.Should().Be("Deep work");
        result.DurationMinutes.Should().Be(45);
        result.Status.Should().Be(SessionStatus.Active);
        result.Timeline!.Breaks.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_PresetNotFound_ReturnsUnknownPresetName()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(DateTime.UtcNow));
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);
        _presets.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);

        var result = await CreateHandler().HandleAsync(new GetActiveSessionQuery());

        result!.PresetName.Should().Be("Unknown");
    }

    [Fact]
    public async Task HandleAsync_WithBreakSchedule_MapsBreaksAndPresetLabel()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(60)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(DateTime.UtcNow), breakSchedule: new BreakTimer(25, 5, 4, 15).ScheduleFor(TimeSpan.FromMinutes(60)),
            breakPresetName: "Classic");
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);
        _presets.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);

        var result = await CreateHandler().HandleAsync(new GetActiveSessionQuery());

        result!.BreakPresetName.Should().Be("Classic");
        result.Timeline!.Breaks.Breaks.Should().ContainSingle().Which.Should().Be(
            new SessionBreak(SessionPhaseKind.ShortBreak, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(30)));
    }
}
