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
    public void CapEndToRedemptionCoverage_FractionalCoverage_RoundsDownNeverUp()
    {
        // Rounding direction matters here specifically because it's the opposite of the
        // existing "round up before persisting a fraction into a timestamp" rule
        // (MODULE_STANDARDS.md) — that rule exists to guarantee "covers at least X" never
        // falls short after tick-rounding. This guarantee runs the other way: duration must
        // never exceed coverage, so rounding must only ever go down.
        var requestedEnd = Start.AddHours(2);
        var entries = new List<RedemptionEntry> { new(Guid.NewGuid(), 45.9) };

        var result = FocusBlock.CapEndToRedemptionCoverage(Start, requestedEnd, entries);

        result.Should().Be(Start.AddMinutes(45));
    }
}
