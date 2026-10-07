using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class RedemptionEntryTests
{
    [Fact]
    public void Constructor_ZeroMinutesCovered_Throws()
    {
        var act = () => new RedemptionEntry(Guid.NewGuid(), WholeMinutes.Of(0));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NegativeMinutesCovered_Throws()
    {
        var act = () => new RedemptionEntry(Guid.NewGuid(), WholeMinutes.Of(-5)); // a negative amount can't even be built

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_PositiveMinutesCovered_DoesNotThrow()
    {
        var missedBlockId = Guid.NewGuid();

        var entry = new RedemptionEntry(missedBlockId, WholeMinutes.Of(30));

        entry.MissedBlockId.Should().Be(missedBlockId);
        entry.Covered.Should().Be(WholeMinutes.Of(30));
    }
}
