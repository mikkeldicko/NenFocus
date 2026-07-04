using FluentAssertions;
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
    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();

    private GetActiveSessionQueryHandler CreateHandler() => new(_sessions, _profiles);

    [Fact]
    public async Task HandleAsync_NoActiveSession_ReturnsNull()
    {
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);

        var result = await CreateHandler().HandleAsync(new GetActiveSessionQuery());

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ActiveSession_MapsProfileNameAndFields()
    {
        var profileId = Guid.NewGuid();
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Deep work"), new SessionDuration(TimeSpan.FromMinutes(45)),
            profileId, SessionOrigin.Spontaneous);
        session.Start(DateTime.UtcNow);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow));

        var result = await CreateHandler().HandleAsync(new GetActiveSessionQuery());

        result.Should().NotBeNull();
        result!.ProfileName.Should().Be("Deep Work");
        result.Goal.Should().Be("Deep work");
        result.DurationMinutes.Should().Be(45);
        result.Status.Should().Be(SessionStatus.Active);
        result.BreakSettings.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ProfileNotFound_ReturnsUnknownProfileName()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(DateTime.UtcNow);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);
        _profiles.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((BlockProfile?)null);

        var result = await CreateHandler().HandleAsync(new GetActiveSessionQuery());

        result!.ProfileName.Should().Be("Unknown");
    }

    [Fact]
    public async Task HandleAsync_WithBreakSettings_MapsToBreakSettingsDto()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous, breakSettings: new BreakSettings(25, 5));
        session.Start(DateTime.UtcNow);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);
        _profiles.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((BlockProfile?)null);

        var result = await CreateHandler().HandleAsync(new GetActiveSessionQuery());

        result!.BreakSettings.Should().NotBeNull();
        result.BreakSettings!.IntervalMinutes.Should().Be(25);
        result.BreakSettings.BreakDurationMinutes.Should().Be(5);
    }
}
