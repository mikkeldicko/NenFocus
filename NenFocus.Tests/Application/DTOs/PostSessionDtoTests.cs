using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.DTOs;

/// <summary>
/// The one builder of the Post-Session screen's data (Gap 95, decision 13): it maps the session
/// and groups its attempts; how long the session counted for is the session's own answer.
/// </summary>
public class PostSessionDtoTests
{
    private static readonly DateTime Start = new(2026, 1, 5, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void From_MapsTheSessionAndGroupsItsAttempts()
    {
        var block = FocusBlock.Spontaneous(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, Start.AddMinutes(60)));
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), block, At.Utc(Start));
        session.Complete(At.Utc(Start.AddMinutes(40)), null);
        BlockedAttempt Attempt(string name, BlockedAttemptType type) =>
            type == BlockedAttemptType.App
                ? BlockedAttempt.ForApp(Guid.NewGuid(), session.Id, new BlockedApp(name), At.Utc(Start.AddMinutes(5)))
                : BlockedAttempt.ForWebsite(Guid.NewGuid(), session.Id, new Website(name), At.Utc(Start.AddMinutes(5)));
        var attempts = new[]
        {
            Attempt("youtube.com", BlockedAttemptType.Website),
            Attempt("youtube.com", BlockedAttemptType.Website),
            Attempt("steam.exe", BlockedAttemptType.App)
        };

        var dto = PostSessionDto.From(session, originatingBlock: block, "Deep Work", attempts);

        dto.SessionId.Should().Be(session.Id);
        dto.Status.Should().Be(SessionStatus.Completed);
        dto.Goal.Should().Be("Write tests");
        dto.PresetName.Should().Be("Deep Work");
        dto.Recorded.Should().Be(At.Slot(Start, Start.AddMinutes(40)));
        dto.PlannedDurationMinutes.Should().Be(60);
        dto.ActualDurationMinutes.Should().Be(40);
        dto.BlockedWebsiteAttempts.Should().BeEquivalentTo(new Dictionary<string, int> { ["youtube.com"] = 2 });
        dto.BlockedAppAttempts.Should().BeEquivalentTo(new Dictionary<string, int> { ["steam"] = 1 });
        dto.TotalBlockedAttempts.Should().Be(3);
    }

    [Fact]
    public void From_APlannedSession_ShowsTheTimeItCountedFor()
    {
        // Ritual 3 minutes in, ran to the end: Post-Session shows the 120 the contract counts.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, Start.AddHours(2)));
        var session = Session.Start(Guid.NewGuid(), new SessionGoal("Write tests"), block, At.Utc(Start.AddMinutes(3)));
        session.Complete(At.Utc(Start.AddHours(2)), block);

        PostSessionDto.From(session, block, "Deep Work", []).ActualDurationMinutes.Should().Be(120);
    }
}
