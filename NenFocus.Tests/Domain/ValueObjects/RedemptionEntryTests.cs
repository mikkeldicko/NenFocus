using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class RedemptionEntryTests
{
    [Fact]
    public void Constructor_ZeroMinutesCovered_Throws()
    {
        var act = () => new RedemptionEntry(Guid.NewGuid(), 0);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NegativeMinutesCovered_Throws()
    {
        var act = () => new RedemptionEntry(Guid.NewGuid(), -5);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_PositiveMinutesCovered_DoesNotThrow()
    {
        var missedBlockId = Guid.NewGuid();

        var entry = new RedemptionEntry(missedBlockId, 30);

        entry.MissedBlockId.Should().Be(missedBlockId);
        entry.MinutesCovered.Should().Be(30);
    }
}
