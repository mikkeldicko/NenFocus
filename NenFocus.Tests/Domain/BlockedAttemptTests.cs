using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Tests.Domain;

public class BlockedAttemptTests
{
    [Fact]
    public void Record_Valid_TrimsResourceNameAndSetsFields()
    {
        var id = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var occurredAt = DateTime.UtcNow;

        var attempt = BlockedAttempt.Record(id, sessionId, "  chrome  ", BlockedAttemptType.App, occurredAt);

        attempt.Id.Should().Be(id);
        attempt.SessionId.Should().Be(sessionId);
        attempt.ResourceName.Should().Be("chrome");
        attempt.AttemptType.Should().Be(BlockedAttemptType.App);
        attempt.OccurredAt.Should().Be(occurredAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Record_BlankResourceName_Throws(string? blank)
    {
        var act = () => BlockedAttempt.Record(
            Guid.NewGuid(), Guid.NewGuid(), blank!, BlockedAttemptType.Website, DateTime.UtcNow);

        act.Should().Throw<ArgumentException>();
    }
}
