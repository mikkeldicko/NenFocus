using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.AcknowledgePostSession;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Sessions;

public class AcknowledgePostSessionCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public AcknowledgePostSessionCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
    }

    private AcknowledgePostSessionCommandHandler CreateHandler() => new(_sessions, _clock);

    private static Session CreateCompletedSession()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(Now.AddMinutes(-30));
        session.Complete(Now);
        return session;
    }

    [Fact]
    public async Task HandleAsync_SessionNotFound_Fails()
    {
        _sessions.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Session?)null);

        var result = await CreateHandler().HandleAsync(
            new AcknowledgePostSessionCommand { SessionId = Guid.NewGuid() });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_CompletedSession_StampsAcknowledgedAtAndSaves()
    {
        var session = CreateCompletedSession();
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateHandler().HandleAsync(new AcknowledgePostSessionCommand { SessionId = session.Id });

        result.Success.Should().BeTrue();
        session.PostSessionAcknowledgedAt.Should().Be(Now);
        await _sessions.Received(1).SaveAsync(session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MissedSession_StampsAcknowledgedAtAndSaves()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("(missed — no goal set)"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Planned, originatingBlockId: Guid.NewGuid());
        var scheduledStart = Now.AddHours(-2);
        session.Miss(scheduledStart, scheduledStart.AddMinutes(30));
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateHandler().HandleAsync(new AcknowledgePostSessionCommand { SessionId = session.Id });

        result.Success.Should().BeTrue();
        session.PostSessionAcknowledgedAt.Should().Be(Now);
    }

    [Fact]
    public async Task HandleAsync_SessionNotCompletedOrMissed_FailsGracefully()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(Now.AddMinutes(-5));
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateHandler().HandleAsync(new AcknowledgePostSessionCommand { SessionId = session.Id });

        result.Success.Should().BeFalse();
        await _sessions.DidNotReceive().SaveAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
    }
}
