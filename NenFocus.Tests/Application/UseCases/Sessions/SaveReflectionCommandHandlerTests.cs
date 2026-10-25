using FluentAssertions;
using NenFocus.Application.UseCases.Sessions.SaveReflection;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.Sessions;

/// <summary>Saving a session's reflection, run as the app runs it: real pipeline, real SQLite (Gap 110).</summary>
public class SaveReflectionCommandHandlerTests : UseCaseTestBase
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public SaveReflectionCommandHandlerTests() => Clock.SetNow(Now);

    private static Session Spontaneous()
    {
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(Now.AddMinutes(-30)));
        return session;
    }

    private Task<SaveReflectionResult> ReflectAsync(Guid sessionId, string reflection) =>
        RunAsync(new SaveReflectionCommand { SessionId = sessionId, Reflection = reflection });

    private async Task<string?> StoredReflectionAsync(Session session) => (await FindAsync<Session>(session.Id))!.Reflection;

    [Fact]
    public async Task SessionNotFound_Fails()
    {
        var result = await ReflectAsync(Guid.NewGuid(), "Went well");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ACompletedSession_StoresTheReflection()
    {
        var session = Spontaneous();
        session.Complete(At.Utc(Now), null);
        await SeedAsync(session);

        var result = await ReflectAsync(session.Id, "Went well");

        result.Success.Should().BeTrue(result.ErrorMessage);
        (await StoredReflectionAsync(session)).Should().Be("Went well");
    }

    [Fact]
    public async Task AMissedSession_StoresTheReflection()
    {
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("(missed — no goal set)"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Planned, originatingBlockId: Guid.NewGuid());
        session.Miss(new TimeSlot(At.Utc(Now.AddHours(-2)), At.Utc(Now.AddHours(-2).AddMinutes(30))));
        await SeedAsync(session);

        var result = await ReflectAsync(session.Id, "Got distracted");

        result.Success.Should().BeTrue(result.ErrorMessage);
        (await StoredReflectionAsync(session)).Should().Be("Got distracted");
    }

    [Fact]
    public async Task ASessionStillRunning_IsRefused_AndNothingIsStored()
    {
        var session = Spontaneous();
        await SeedAsync(session);

        var result = await ReflectAsync(session.Id, "Too soon");

        result.Success.Should().BeFalse();
        (await StoredReflectionAsync(session)).Should().BeNull();
    }
}
