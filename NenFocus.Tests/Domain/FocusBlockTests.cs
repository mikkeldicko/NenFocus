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

        var result = FocusBlock.CapEndToRedemptionCoverage(Start, requestedEnd, []);

        result.Should().Be(requestedEnd);
    }

    [Fact]
    public void CapEndToRedemptionCoverage_EntriesCoverAtLeastTheRequestedDuration_ReturnsRequestedEndUnchanged()
    {
        var requestedEnd = Start.AddHours(1); // 60 min requested
        var entries = new List<RedemptionEntry> { new(Guid.NewGuid(), 60) };

        var result = FocusBlock.CapEndToRedemptionCoverage(Start, requestedEnd, entries);

        result.Should().Be(requestedEnd);
    }

    [Fact]
    public void CapEndToRedemptionCoverage_EntriesCoverMoreThanRequested_StillReturnsRequestedEndUnchanged()
    {
        // Covering more than requested is fine — the block just claims less than what's
        // available. Only "duration exceeds coverage" needs capping, never the reverse.
        var requestedEnd = Start.AddMinutes(30);
        var entries = new List<RedemptionEntry> { new(Guid.NewGuid(), 90) };

        var result = FocusBlock.CapEndToRedemptionCoverage(Start, requestedEnd, entries);

        result.Should().Be(requestedEnd);
    }

    [Fact]
    public void CapEndToRedemptionCoverage_RequestedDurationExceedsCoverage_CapsToWhatIsCovered()
    {
        var requestedEnd = Start.AddHours(2); // 120 min requested
        var entries = new List<RedemptionEntry> { new(Guid.NewGuid(), 45) };

        var result = FocusBlock.CapEndToRedemptionCoverage(Start, requestedEnd, entries);

        result.Should().Be(Start.AddMinutes(45));
    }

    [Fact]
    public void CapEndToRedemptionCoverage_MultipleEntries_CapsToTheirSum()
    {
        var requestedEnd = Start.AddHours(2);
        var entries = new List<RedemptionEntry> { new(Guid.NewGuid(), 20), new(Guid.NewGuid(), 25) };

        var result = FocusBlock.CapEndToRedemptionCoverage(Start, requestedEnd, entries);

        result.Should().Be(Start.AddMinutes(45));
    }

    [Fact]
    public void CapEndToRedemptionCoverage_FractionalCoverage_CapsToTheExactAmountNotAWholeMinute()
    {
        // No rounding here at all — the cap trims to precisely what the entries claim.
        // RedemptionEntry.MinutesCovered is never re-derived from this block's own
        // persisted End, so there's no "at least X survives tick-rounding" concern
        // (MODULE_STANDARDS.md) to protect against; flooring away the fraction (the old
        // behavior) only threw away real, already-earned coverage.
        var requestedEnd = Start.AddHours(2);
        var entries = new List<RedemptionEntry> { new(Guid.NewGuid(), 45.9) };

        var result = FocusBlock.CapEndToRedemptionCoverage(Start, requestedEnd, entries);

        result.Should().Be(Start.AddMinutes(45.9));
    }

    [Fact]
    public void CapEndToRedemptionCoverage_TinyResidualDeficit_CapsToASubMinuteEndStillAfterStart()
    {
        // Regression test: flooring a sub-1-minute coverage sum used to round to zero
        // minutes, making the capped End equal Start and crashing FocusBlock.Create with
        // "End time must be after start time." — reproduces the reported bug (a residual
        // deficit of a few seconds left after an earlier redemption block).
        var requestedEnd = Start.AddMinutes(1); // suggestion padded up to a whole minute
        var entries = new List<RedemptionEntry> { new(Guid.NewGuid(), 0.0667) }; // ~4 seconds owed

        var result = FocusBlock.CapEndToRedemptionCoverage(Start, requestedEnd, entries);

        result.Should().Be(Start.AddMinutes(0.0667));
        result.Should().BeAfter(Start);
    }

    [Fact]
    public void WasRedemptionCoverageMeaningfullyCapped_SubMinuteTrim_ReturnsFalse()
    {
        // The routine case: a Cover Deficit suggestion's whole-minute safety margin gets
        // trimmed back off. Not worth surfacing to the user every time.
        var requestedEnd = Start.AddMinutes(2);
        var cappedEnd = Start.AddMinutes(1.0667);

        FocusBlock.WasRedemptionCoverageMeaningfullyCapped(requestedEnd, cappedEnd).Should().BeFalse();
    }

    [Fact]
    public void WasRedemptionCoverageMeaningfullyCapped_WholeMinuteOrMoreTrim_ReturnsTrue()
    {
        // A genuine overreach — e.g. a manually resized redemption block stretched well
        // past what its entries actually cover.
        var requestedEnd = Start.AddHours(2);
        var cappedEnd = Start.AddMinutes(45);

        FocusBlock.WasRedemptionCoverageMeaningfullyCapped(requestedEnd, cappedEnd).Should().BeTrue();
    }

    [Fact]
    public void RawDeficitMinutes_NothingCredited_ReturnsFullScheduledDuration()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddHours(1));

        block.RawDeficitMinutes(creditedMinutes: 0).Should().Be(60);
    }

    [Fact]
    public void RawDeficitMinutes_PartiallyCredited_ReturnsShortfallOnly()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddHours(1));

        block.RawDeficitMinutes(creditedMinutes: 20).Should().Be(40);
    }

    [Fact]
    public void RawDeficitMinutes_FullyCredited_ReturnsZero_NeverNegative()
    {
        // Credited can legitimately exceed the scheduled window (e.g. a session that ran
        // past the block's own end) — the deficit floors at zero rather than going negative.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddHours(1));

        block.RawDeficitMinutes(creditedMinutes: 75).Should().Be(0);
    }

    // --- IsAwaitingRitual ---

    [Fact]
    public void IsAwaitingRitual_ActiveAndRitualNotCompleted_ReturnsTrue()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddHours(1));

        block.IsAwaitingRitual(Start.AddMinutes(10)).Should().BeTrue();
    }

    [Fact]
    public void IsAwaitingRitual_RitualAlreadyCompleted_ReturnsFalse()
    {
        // The race this replaces FocusSessionCompleted for: the block is still inside its
        // window at `at`, its session has just completed, nothing is running — the Ritual
        // must not be prompted again for a block that already had one.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddHours(1));
        block.MarkRitualCompleted(Start);

        block.IsAwaitingRitual(Start.AddMinutes(59)).Should().BeFalse();
    }

    [Fact]
    public void IsAwaitingRitual_BeforeStart_ReturnsFalse()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddHours(1));

        block.IsAwaitingRitual(Start.AddSeconds(-1)).Should().BeFalse();
    }

    [Fact]
    public void IsAwaitingRitual_AtOrAfterEnd_ReturnsFalse()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddHours(1));

        block.IsAwaitingRitual(Start.AddHours(1)).Should().BeFalse();
    }

    [Fact]
    public void IsAwaitingRitual_Cancelled_ReturnsFalse()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddHours(1));
        block.MarkCancelled(Start);

        block.IsAwaitingRitual(Start.AddMinutes(10)).Should().BeFalse();
    }
}
