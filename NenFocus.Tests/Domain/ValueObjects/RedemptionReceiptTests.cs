using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class RedemptionReceiptTests
{
    [Fact]
    public void None_HasNoEntriesAndCoversNothing()
    {
        RedemptionReceipt.None.Entries.Should().BeEmpty();
        RedemptionReceipt.None.TotalMinutes.Should().Be(0);
    }

    [Fact]
    public void Constructor_SameMissedBlockListedTwice_Throws()
    {
        var missedBlockId = Guid.NewGuid();

        var act = () => new RedemptionReceipt([new(missedBlockId, 20), new(missedBlockId, 10)]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TotalMinutes_IsTheSumOfEveryEntry()
    {
        var receipt = new RedemptionReceipt([new(Guid.NewGuid(), 20), new(Guid.NewGuid(), 25.5)]);

        receipt.TotalMinutes.Should().Be(45.5);
    }

    [Fact]
    public void MinutesFor_ListedMissedBlock_ReturnsItsEntry()
    {
        var missedBlockId = Guid.NewGuid();
        var receipt = new RedemptionReceipt([new(Guid.NewGuid(), 20), new(missedBlockId, 25)]);

        receipt.MinutesFor(missedBlockId).Should().Be(25);
    }

    [Fact]
    public void MinutesFor_UnlistedMissedBlock_IsZero()
    {
        var receipt = new RedemptionReceipt([new(Guid.NewGuid(), 20)]);

        receipt.MinutesFor(Guid.NewGuid()).Should().Be(0);
    }

    // ── SplitAt: dividing across a split, in time order (Gap 106, decision 2) ──

    [Fact]
    public void SplitAt_AnEntryInsideTheFirstPiece_GoesOnlyToTheFirst()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var receipt = new RedemptionReceipt([new(a, 15), new(b, 30)]);

        var (first, second) = receipt.SplitAt(TimeSpan.FromMinutes(60));

        first.Entries.Should().Equal(new RedemptionEntry(a, 15), new RedemptionEntry(b, 30));
        second.Should().Be(RedemptionReceipt.None);
    }

    [Fact]
    public void SplitAt_AnEntryStraddlingTheCut_IsSplitInTwo()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var receipt = new RedemptionReceipt([new(a, 40), new(b, 50)]);

        var (first, second) = receipt.SplitAt(TimeSpan.FromMinutes(60));

        first.Entries.Should().Equal(new RedemptionEntry(a, 40), new RedemptionEntry(b, 20));
        second.Entries.Should().Equal(new RedemptionEntry(b, 30));
    }

    [Fact]
    public void SplitAt_WithSurplus_TheFirstFillsFirst_NothingIsLost()
    {
        // A 2h block covering 1h30, cut at 1h: 1h | 30m covered (Design Review Rule 16's hardest case).
        var a = Guid.NewGuid();
        var receipt = new RedemptionReceipt([new(a, 90)]);

        var (first, second) = receipt.SplitAt(TimeSpan.FromHours(1));

        first.Entries.Should().Equal(new RedemptionEntry(a, 60));
        second.Entries.Should().Equal(new RedemptionEntry(a, 30));
        (first.TotalMinutes + second.TotalMinutes).Should().Be(receipt.TotalMinutes);
    }

    [Fact]
    public void Equality_SameEntriesInTheSameOrder_AreEqual()
    {
        var missedBlockId = Guid.NewGuid();

        new RedemptionReceipt([new(missedBlockId, 20)])
            .Should().Be(new RedemptionReceipt([new(missedBlockId, 20)]));
    }
}
