using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain;

public class FocusBlockTests
{
    private static readonly DateTime Start = new(2026, 1, 5, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CapEndToRedemptionCoverage_NoEntries_ReturnsRequestedEndUnchanged()
    {
        var requestedEnd = Start.AddHours(2);

        var result = FocusBlock.CapEndToRedemptionCoverage(At.Slot(Start, requestedEnd), new RedemptionReceipt([]));

        result.Should().Be(At.Slot(Start, requestedEnd));
    }

    [Fact]
    public void CapEndToRedemptionCoverage_EntriesCoverAtLeastTheRequestedDuration_ReturnsRequestedEndUnchanged()
    {
        var requestedEnd = Start.AddHours(1); // 60 min requested
        var entries = new List<RedemptionEntry> { new(Guid.NewGuid(), WholeMinutes.Of(60)) };

        var result = FocusBlock.CapEndToRedemptionCoverage(At.Slot(Start, requestedEnd), new RedemptionReceipt(entries));

        result.Should().Be(At.Slot(Start, requestedEnd));
    }

    [Fact]
    public void CapEndToRedemptionCoverage_EntriesCoverMoreThanRequested_StillReturnsRequestedEndUnchanged()
    {
        // Covering more than requested is fine — the block just claims less than what's
        // available. Only "duration exceeds coverage" needs capping, never the reverse.
        var requestedEnd = Start.AddMinutes(30);
        var entries = new List<RedemptionEntry> { new(Guid.NewGuid(), WholeMinutes.Of(90)) };

        var result = FocusBlock.CapEndToRedemptionCoverage(At.Slot(Start, requestedEnd), new RedemptionReceipt(entries));

        result.Should().Be(At.Slot(Start, requestedEnd));
    }

    [Fact]
    public void CapEndToRedemptionCoverage_RequestedDurationExceedsCoverage_CapsToWhatIsCovered()
    {
        var requestedEnd = Start.AddHours(2); // 120 min requested
        var entries = new List<RedemptionEntry> { new(Guid.NewGuid(), WholeMinutes.Of(45)) };

        var result = FocusBlock.CapEndToRedemptionCoverage(At.Slot(Start, requestedEnd), new RedemptionReceipt(entries));

        result.Should().Be(At.Slot(Start, Start.AddMinutes(45)));
    }

    [Fact]
    public void CapEndToRedemptionCoverage_MultipleEntries_CapsToTheirSum()
    {
        var requestedEnd = Start.AddHours(2);
        var entries = new List<RedemptionEntry> { new(Guid.NewGuid(), WholeMinutes.Of(20)), new(Guid.NewGuid(), WholeMinutes.Of(25)) };

        var result = FocusBlock.CapEndToRedemptionCoverage(At.Slot(Start, requestedEnd), new RedemptionReceipt(entries));

        result.Should().Be(At.Slot(Start, Start.AddMinutes(45)));
    }

    // --- IsAwaitingRitual ---

    [Fact]
    public void IsAwaitingRitual_ActiveAndRitualNotCompleted_ReturnsTrue()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, Start.AddHours(1)));

        block.IsAwaitingRitual(At.Utc(Start.AddMinutes(10))).Should().BeTrue();
    }

    [Fact]
    public void IsAwaitingRitual_RitualAlreadyCompleted_ReturnsFalse()
    {
        // The race this replaces FocusSessionCompleted for: the block is still inside its
        // window at `at`, its session has just completed, nothing is running — the Ritual
        // must not be prompted again for a block that already had one.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, Start.AddHours(1)));
        block.MarkRitualCompleted(At.Utc(Start));

        block.IsAwaitingRitual(At.Utc(Start.AddMinutes(59))).Should().BeFalse();
    }

    [Fact]
    public void IsAwaitingRitual_BeforeStart_ReturnsFalse()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, Start.AddHours(1)));

        block.IsAwaitingRitual(At.Utc(Start.AddSeconds(-1))).Should().BeFalse();
    }

    [Fact]
    public void IsAwaitingRitual_AtOrAfterEnd_ReturnsFalse()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, Start.AddHours(1)));

        block.IsAwaitingRitual(At.Utc(Start.AddHours(1))).Should().BeFalse();
    }

    [Fact]
    public void IsAwaitingRitual_Cancelled_ReturnsFalse()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Start, Start.AddHours(1)));
        block.MarkCancelled(At.Utc(Start));

        block.IsAwaitingRitual(At.Utc(Start.AddMinutes(10))).Should().BeFalse();
    }

    // ── SplitInto: a split never recalculates (Gap 106, decisions 2 and 16) ──

    [Fact]
    public void SplitInto_ARedemptionBlockWithSurplus_DividesItsReceiptInTimeOrder_AndShortensNothing()
    {
        // 2h covering 1h30, cut at 1h → 1h | 30m covered; the pieces keep the requested slots.
        var missed = Guid.NewGuid();
        var membership = TestContracts.Redemption(Guid.NewGuid());
        var block = FocusBlock.CreateWithReceipt(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, Start.AddHours(2)), "Title",
            membership, receipt: new RedemptionReceipt([new(missed, WholeMinutes.Of(90))]));
        var firstSlot = At.Slot(Start, Start.AddHours(1));
        var secondSlot = At.Slot(Start.AddHours(3), Start.AddHours(4));
        var (firstId, secondId) = (Guid.NewGuid(), Guid.NewGuid());

        var (first, second) = block.SplitInto(firstId, firstSlot, secondId, secondSlot);

        first.Id.Should().Be(firstId);
        second.Id.Should().Be(secondId);
        first.Scheduled.Should().Be(firstSlot);
        second.Scheduled.Should().Be(secondSlot);
        first.Receipt.Entries.Should().Equal(new RedemptionEntry(missed, WholeMinutes.Of(60)));
        second.Receipt.Entries.Should().Equal(new RedemptionEntry(missed, WholeMinutes.Of(30)));
        foreach (var piece in new[] { first, second })
        {
            piece.Membership.Should().Be(membership);
            piece.BlockPresetId.Should().Be(block.BlockPresetId);
            piece.Title.Should().Be("Title");
        }
    }

    [Fact]
    public void SplitInto_ACommittedBlock_PiecesKeepItsMembership_AndCoverNothing()
    {
        var membership = TestContracts.Committed(Guid.NewGuid());
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, Start.AddHours(3)), membership: membership);

        var (first, second) = block.SplitInto(
            Guid.NewGuid(), At.Slot(Start, Start.AddHours(2)), Guid.NewGuid(), At.Slot(Start.AddHours(2), Start.AddHours(3)));

        first.Membership.Should().Be(membership);
        second.Membership.Should().Be(membership);
        first.Receipt.Should().Be(RedemptionReceipt.None);
        second.Receipt.Should().Be(RedemptionReceipt.None);
    }

    [Fact]
    public void SplitInto_PiecesThatDoNotAddUpToTheOriginal_Throws()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, Start.AddHours(2)),
            membership: TestContracts.Committed(Guid.NewGuid()));

        var act = () => block.SplitInto(
            Guid.NewGuid(), At.Slot(Start, Start.AddHours(1)), Guid.NewGuid(), At.Slot(Start.AddHours(1), Start.AddMinutes(90)));

        act.Should().Throw<ArgumentException>();
    }
}
