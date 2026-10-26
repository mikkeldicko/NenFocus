using FluentAssertions;
using NenFocus.Application.UseCases.Sessions.AcknowledgePostSession;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.Sessions;

/// <summary>Dismissing the post-session screen, run as the app runs it: real pipeline, real SQLite (Gap 110).</summary>
public class AcknowledgePostSessionCommandHandlerTests : UseCaseTestBase
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public AcknowledgePostSessionCommandHandlerTests() => Clock.SetNow(Now);

    private static Session Spontaneous()
    {
        return Session.Start(Guid.NewGuid(), new SessionGoal("Goal"),
            FocusBlock.Spontaneous(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddMinutes(-30), Now)),
            At.Utc(Now.AddMinutes(-30)));
    }

    private Task<AcknowledgePostSessionResult> AcknowledgeAsync(Guid sessionId) =>
        RunAsync(new AcknowledgePostSessionCommand { SessionId = sessionId });

    private async Task<Moment?> StoredAcknowledgementAsync(Session session) =>
        (await FindAsync<Session>(session.Id))!.Reflection.AcknowledgedAt;

    [Fact]
    public async Task SessionNotFound_Fails()
    {
        var result = await AcknowledgeAsync(Guid.NewGuid());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ACompletedSession_IsStampedAcknowledged()
    {
        var session = Spontaneous();
        session.Complete(At.Utc(Now), null);
        await SeedAsync(session);

        var result = await AcknowledgeAsync(session.Id);

        result.Success.Should().BeTrue(result.ErrorMessage);
        (await StoredAcknowledgementAsync(session)).Should().Be(At.Utc(Now));
    }

    [Fact]
    public async Task ASessionStillRunning_IsRefused_AndNotStamped()
    {
        var session = Spontaneous();
        await SeedAsync(session);

        var result = await AcknowledgeAsync(session.Id);

        result.Success.Should().BeFalse();
        (await StoredAcknowledgementAsync(session)).Should().BeNull();
    }
}
