using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.SaveReflection;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Sessions;

public class SaveReflectionCommandHandlerTests
{
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();

    private SaveReflectionCommandHandler CreateHandler() => new(_sessions);

    private static Session CreateCompletedSession()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(DateTime.UtcNow.AddMinutes(-30));
        session.Complete(DateTime.UtcNow);
        return session;
    }

    [Fact]
    public async Task HandleAsync_SessionNotFound_Fails()
    {
        _sessions.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Session?)null);

        var result = await CreateHandler().HandleAsync(
            new SaveReflectionCommand { SessionId = Guid.NewGuid(), Reflection = "Went well" });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_HappyPath_SetsReflectionAndSaves()
    {
        var session = CreateCompletedSession();
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateHandler().HandleAsync(
            new SaveReflectionCommand { SessionId = session.Id, Reflection = "Went well" });

        result.Success.Should().BeTrue();
        session.Reflection.Should().Be("Went well");
        await _sessions.Received(1).SaveAsync(session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SessionNotCompleted_FailsGracefully()
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(DateTime.UtcNow.AddMinutes(-5));
        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateHandler().HandleAsync(
            new SaveReflectionCommand { SessionId = session.Id, Reflection = "Too soon" });

        result.Success.Should().BeFalse();
        await _sessions.DidNotReceive().SaveAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
    }
}
