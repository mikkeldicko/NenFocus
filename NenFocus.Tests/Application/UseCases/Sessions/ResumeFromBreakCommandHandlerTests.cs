using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.ResumeFromBreak;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Sessions;

public class ResumeFromBreakCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly ISessionStateService _sessionState = Substitute.For<ISessionStateService>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public ResumeFromBreakCommandHandlerTests() => _clock.UtcNow.Returns(Now);

    private ResumeFromBreakCommandHandler CreateHandler() => new(_sessions, _sessionState, _clock);

    private static Session CreateOnBreakSession()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous, breakTimer: new BreakTimer(25, 5, 4, 15));
        session.Start(Now.AddMinutes(-10));
        session.StartBreak(Now.AddMinutes(-2), SessionPhaseKind.ShortBreak);
        return session;
    }

    [Fact]
    public async Task HandleAsync_SessionNotFound_Fails()
    {
        _sessions.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Session?)null);

        var result = await CreateHandler().HandleAsync(new ResumeFromBreakCommand { SessionId = Guid.NewGuid() });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_SessionNotOnBreak_Fails()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(Now.AddMinutes(-5));
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateHandler().HandleAsync(new ResumeFromBreakCommand { SessionId = session.Id });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_HappyPath_ResumesAndSaves()
    {
        var session = CreateOnBreakSession();
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _sessionState.ActiveSession.Returns(new SessionDto { Id = session.Id, Status = SessionStatus.OnBreak });

        var result = await CreateHandler().HandleAsync(new ResumeFromBreakCommand { SessionId = session.Id });

        result.Success.Should().BeTrue();
        session.Status.Should().Be(SessionStatus.Active);
        await _sessions.Received(1).SaveAsync(session, Arg.Any<CancellationToken>());
        _sessionState.Received(1).SetActive(Arg.Is<SessionDto>(d =>
            d.Status == SessionStatus.Active && d.BreakStartedAt == null));
    }

    [Fact]
    public async Task HandleAsync_NoActiveSessionStateCached_SkipsSetActiveButStillSucceeds()
    {
        var session = CreateOnBreakSession();
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _sessionState.ActiveSession.Returns((SessionDto?)null);

        var result = await CreateHandler().HandleAsync(new ResumeFromBreakCommand { SessionId = session.Id });

        result.Success.Should().BeTrue();
        _sessionState.DidNotReceive().SetActive(Arg.Any<SessionDto>());
    }

    [Fact]
    public async Task HandleAsync_HappyPath_RaisesBreakEndedAtClockTime()
    {
        var session = CreateOnBreakSession();
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        await CreateHandler().HandleAsync(new ResumeFromBreakCommand { SessionId = session.Id });

        session.DomainEvents.OfType<BreakEndedEvent>().Single().OccurredAt.Should().Be(Now);
    }
}
