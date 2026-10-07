using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class RedemptionReceiptTests
{
    [Fact]
    public void None_HasNoEntriesAndCoversNothing()
    {
        RedemptionReceipt.None.Entries.Should().BeEmpty();
        RedemptionReceipt.None.Total.Should().Be(WholeMinutes.Of(0));
    }

    [Fact]
    public void Constructor_SameMissedBlockListedTwice_Throws()
    {
        var missedBlockId = Guid.NewGuid();

        var act = () => new RedemptionReceipt([new(missedBlockId, WholeMinutes.Of(20)), new(missedBlockId, WholeMinutes.Of(10))]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Total_IsTheSumOfEveryEntry()
    {
        var receipt = new RedemptionReceipt([new(Guid.NewGuid(), WholeMinutes.Of(20)), new(Guid.NewGuid(), WholeMinutes.Of(25))]);

        receipt.Total.Should().Be(WholeMinutes.Of(45));
    }

    [Fact]
    public void CoveredFor_ListedMissedBlock_ReturnsItsEntry()
    {
        var missedBlockId = Guid.NewGuid();
        var receipt = new RedemptionReceipt([new(Guid.NewGuid(), WholeMinutes.Of(20)), new(missedBlockId, WholeMinutes.Of(25))]);

        receipt.CoveredFor(missedBlockId).Should().Be(WholeMinutes.Of(25));
    }

    [Fact]
    public void CoveredFor_UnlistedMissedBlock_IsZero()
    {
        var receipt = new RedemptionReceipt([new(Guid.NewGuid(), WholeMinutes.Of(20))]);

        receipt.CoveredFor(Guid.NewGuid()).Should().Be(WholeMinutes.Zero);
    }

    // ── SplitAt: dividing across a split, in time order (Gap 106, decision 2) ──

    [Fact]
    public void SplitAt_AnEntryInsideTheFirstPiece_GoesOnlyToTheFirst()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var receipt = new RedemptionReceipt([new(a, WholeMinutes.Of(15)), new(b, WholeMinutes.Of(30))]);

        var (first, second) = receipt.SplitAt(WholeMinutes.Of(60));

        first.Entries.Should().Equal(new RedemptionEntry(a, WholeMinutes.Of(15)), new RedemptionEntry(b, WholeMinutes.Of(30)));
        second.Should().Be(RedemptionReceipt.None);
    }

    [Fact]
    public void SplitAt_AnEntryStraddlingTheCut_IsSplitInTwo()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var receipt = new RedemptionReceipt([new(a, WholeMinutes.Of(40)), new(b, WholeMinutes.Of(50))]);

        var (first, second) = receipt.SplitAt(WholeMinutes.Of(60));

        first.Entries.Should().Equal(new RedemptionEntry(a, WholeMinutes.Of(40)), new RedemptionEntry(b, WholeMinutes.Of(20)));
        second.Entries.Should().Equal(new RedemptionEntry(b, WholeMinutes.Of(30)));
    }

    [Fact]
    public void SplitAt_WithSurplus_TheFirstFillsFirst_NothingIsLost()
    {
        // A 2h block covering 1h30, cut at 1h: 1h | 30m covered (Design Review Rule 16's hardest case).
        var a = Guid.NewGuid();
        var receipt = new RedemptionReceipt([new(a, WholeMinutes.Of(90))]);

        var (first, second) = receipt.SplitAt(WholeMinutes.Of(60));

        first.Entries.Should().Equal(new RedemptionEntry(a, WholeMinutes.Of(60)));
        second.Entries.Should().Equal(new RedemptionEntry(a, WholeMinutes.Of(30)));
        (first.Total + second.Total).Should().Be(receipt.Total);
    }

    [Fact]
    public void Equality_SameEntriesInTheSameOrder_AreEqual()
    {
        var missedBlockId = Guid.NewGuid();

        new RedemptionReceipt([new(missedBlockId, WholeMinutes.Of(20))])
            .Should().Be(new RedemptionReceipt([new(missedBlockId, WholeMinutes.Of(20))]));
    }
}
