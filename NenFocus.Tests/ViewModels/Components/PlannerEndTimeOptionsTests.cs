using FluentAssertions;
using NenFocus.ViewModels.Components;

namespace NenFocus.Tests.ViewModels.Components;

public class PlannerEndTimeOptionsTests
{
    [Fact]
    public void GenerateOffsets_FirstHour_IsFifteenMinuteSteps()
    {
        var offsets = PlannerEndTimeOptions.GenerateOffsets(isFocus: true);

        offsets.Take(4).Should().Equal(
            TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(45), TimeSpan.FromMinutes(60));
    }

    [Fact]
    public void GenerateOffsets_AfterFirstHour_IsThirtyMinuteSteps()
    {
        var offsets = PlannerEndTimeOptions.GenerateOffsets(isFocus: true);

        offsets.Skip(4).Take(3).Should().Equal(
            TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(120), TimeSpan.FromMinutes(150));
    }

    [Fact]
    public void GenerateOffsets_Focus_TruncatesAtExactlyTwelveHours()
    {
        var offsets = PlannerEndTimeOptions.GenerateOffsets(isFocus: true);

        offsets.Should().Contain(TimeSpan.FromHours(12));
        offsets.Should().NotContain(o => o > TimeSpan.FromHours(12));
        offsets.Last().Should().Be(TimeSpan.FromHours(12));
    }

    [Fact]
    public void GenerateOffsets_Background_HasNoTwelveHourCap()
    {
        var offsets = PlannerEndTimeOptions.GenerateOffsets(isFocus: false);

        offsets.Should().Contain(o => o > TimeSpan.FromHours(12));
    }

    [Fact]
    public void GenerateOffsets_Background_TruncatesJustShortOfTwentyFourHours()
    {
        var offsets = PlannerEndTimeOptions.GenerateOffsets(isFocus: false);

        offsets.Last().Should().Be(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(30));
        offsets.Should().NotContain(TimeSpan.FromHours(24));
    }

    [Fact]
    public void GenerateOffsets_IsIndependentOfStartsOwnMinuteAlignment()
    {
        // The offset sequence is purely duration-based — confirmed against two worked
        // examples during design (10:15 start, 10:35 start) — so it never varies with
        // what Start itself happens to be; this test documents that invariant directly.
        var offsetsForFocus1 = PlannerEndTimeOptions.GenerateOffsets(isFocus: true);
        var offsetsForFocus2 = PlannerEndTimeOptions.GenerateOffsets(isFocus: true);

        offsetsForFocus1.Should().Equal(offsetsForFocus2);
    }

    [Fact]
    public void GenerateOffsets_WithAMaximum_StopsAtItAndOffersItExactly()
    {
        // A Cover Deficit tile may be shortened, never lengthened past the debt (Gap 106,
        // decision 19): a 37-minute debt offers 15, 30 and 37.
        var offsets = PlannerEndTimeOptions.GenerateOffsets(isFocus: true, maximum: TimeSpan.FromMinutes(37));

        offsets.Should().Equal(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(37));
    }

    [Fact]
    public void GenerateOffsets_WithAMaximumOnAStep_OffersItOnce()
    {
        var offsets = PlannerEndTimeOptions.GenerateOffsets(isFocus: true, maximum: TimeSpan.FromMinutes(90));

        offsets.Should().Equal(
            TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(45), TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(90));
    }
}
