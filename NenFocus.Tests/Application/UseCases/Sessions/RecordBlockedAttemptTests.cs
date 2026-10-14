using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NenFocus.Application.UseCases.Sessions.RecordBlockedAttempt;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.Sessions;

/// <summary>
/// Recording what enforcement saw, run as the app runs it: real pipeline, real SQLite (Gap 110).
/// Enforcement reports the thing and the policy it was enforcing; this decides what the attempt
/// belongs to and whether it's a new one (Gap 112, I3).
/// </summary>
public class RecordBlockedAttemptTests : UseCaseTestBase
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid SessionId = Guid.NewGuid();

    private static ActivePolicySlice EnforcingFor(Guid? sessionId) => new() { ActiveSessionId = sessionId };

    private static RecordBlockedAttemptCommand Saw(
        string resource, BlockedAttemptType kind = BlockedAttemptType.Website, DateTime? at = null, Guid? sessionId = null) => new()
    {
        Resource = resource,
        Kind = kind,
        OccurredAt = At.Utc(at ?? Now),
        Policy = EnforcingFor(sessionId ?? SessionId),
    };

    private Task<List<BlockedAttempt>> StoredAsync() =>
        ReadAsync(context => context.BlockedAttempts.ToListAsync());

    [Fact]
    public async Task AnAttemptInASession_IsStoredAsTheListKnowsIt()
    {
        var result = await RunAsync(Saw("Steam", BlockedAttemptType.App));

        result.Recorded.Should().BeTrue();
        var stored = (await StoredAsync()).Should().ContainSingle().Subject;
        stored.SessionId.Should().Be(SessionId);
        stored.ResourceName.Should().Be("steam");
        stored.AttemptType.Should().Be(BlockedAttemptType.App);
        stored.OccurredAt.Should().Be(At.Utc(Now));
    }

    [Fact]
    public async Task ItBelongsToTheSessionThePolicyWasEnforcingFor_NotWhateverRunsNow()
    {
        // Attempts are queued: by the time this runs, that session may have ended (nothing runs here).
        var ended = Guid.NewGuid();

        await RunAsync(Saw("youtube.com", sessionId: ended));

        (await StoredAsync()).Should().ContainSingle().Which.SessionId.Should().Be(ended);
    }

    [Fact]
    public async Task OutsideASession_NothingIsStored()
    {
        // Only attempts inside a session are tracked today — Background blocks are Gap 117's.
        var result = await RunAsync(Saw("youtube.com") with { Policy = EnforcingFor(null) });

        result.Recorded.Should().BeFalse();
        (await StoredAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task TheSameThingAgainWithinTwoSeconds_IsNotANewAttempt()
    {
        await RunAsync(Saw("youtube.com"));

        var again = await RunAsync(Saw("youtube.com", at: Now.AddSeconds(1)));

        again.Recorded.Should().BeFalse();
        (await StoredAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task TheSameThingAgainLater_IsANewAttempt()
    {
        await RunAsync(Saw("youtube.com"));

        await RunAsync(Saw("youtube.com", at: Now.AddSeconds(5)));

        (await StoredAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task SomethingThatIsNoAppOrSite_IsNotAnAttempt()
    {
        var result = await RunAsync(Saw("https://"));

        result.Recorded.Should().BeFalse();
        (await StoredAsync()).Should().BeEmpty();
    }
}
