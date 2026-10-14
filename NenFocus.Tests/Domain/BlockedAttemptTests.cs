using FluentAssertions;
using NenFocus.Domain.ValueObjects;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Tests.Domain;

/// <summary>
/// One attempt to reach a blocked app or site during a session. Recorded through the value object it
/// was (Gap 112: the list's own definition of an app or site), and a quick repeat of the same thing —
/// a redirect chain, a respawning app — is not a new attempt.
/// </summary>
public class BlockedAttemptTests
{
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static BlockedAttempt App(string name, DateTime at, Guid? sessionId = null) =>
        BlockedAttempt.ForApp(Guid.NewGuid(), sessionId ?? SessionId, new BlockedApp(name), At.Utc(at));

    private static BlockedAttempt Site(string url, DateTime at) =>
        BlockedAttempt.ForWebsite(Guid.NewGuid(), SessionId, new Website(url), At.Utc(at));

    [Fact]
    public void ForApp_RecordsTheAppAsTheListKnowsIt()
    {
        var id = Guid.NewGuid();

        var attempt = BlockedAttempt.ForApp(id, SessionId, new BlockedApp(@"C:\Games\Steam.exe"), At.Utc(Now));

        attempt.Id.Should().Be(id);
        attempt.SessionId.Should().Be(SessionId);
        attempt.ResourceName.Should().Be("steam");
        attempt.AttemptType.Should().Be(BlockedAttemptType.App);
        attempt.OccurredAt.Should().Be(At.Utc(Now));
    }

    [Fact]
    public void ForWebsite_RecordsTheSiteAsTheListKnowsIt()
    {
        var attempt = Site("https://www.YouTube.com/watch", Now);

        attempt.ResourceName.Should().Be("youtube.com");
        attempt.AttemptType.Should().Be(BlockedAttemptType.Website);
    }

    [Fact]
    public void TheSameThingAgainWithinTwoSeconds_IsARepeat()
    {
        App("steam", Now.AddMilliseconds(1999)).IsRepeatOf(App("steam", Now)).Should().BeTrue();
    }

    [Fact]
    public void TheSameThingAgainTwoSecondsLater_IsANewAttempt()
    {
        App("steam", Now.AddSeconds(2)).IsRepeatOf(App("steam", Now)).Should().BeFalse();
    }

    [Fact]
    public void SomethingElse_IsNeverARepeat()
    {
        App("discord", Now).IsRepeatOf(App("steam", Now)).Should().BeFalse();
        Site("steam.com", Now).IsRepeatOf(Site("youtube.com", Now)).Should().BeFalse();
    }

    [Fact]
    public void TheSameThingInAnotherSession_IsNeverARepeat()
    {
        App("steam", Now, Guid.NewGuid()).IsRepeatOf(App("steam", Now)).Should().BeFalse();
    }
}
