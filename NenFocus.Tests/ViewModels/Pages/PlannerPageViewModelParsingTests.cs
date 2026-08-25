using FluentAssertions;
using NenFocus.ViewModels.Pages;

namespace NenFocus.Tests.ViewModels.Pages;

public class PlannerPageViewModelParsingTests
{
    [Theory]
    [InlineData("09:00", 9, 0)]
    [InlineData("23:59", 23, 59)]
    [InlineData("00:00", 0, 0)]
    public void TryParseFormTime_OrdinaryTime_Succeeds(string input, int hour, int minute)
    {
        var succeeded = PlannerPageViewModel.TryParseFormTime(input, out var result);

        succeeded.Should().BeTrue();
        result.Should().Be(new TimeSpan(hour, minute, 0));
    }

    [Fact]
    public void TryParseFormTime_TwentyFourHundred_SucceedsAsTwentyFourHours()
    {
        var succeeded = PlannerPageViewModel.TryParseFormTime("24:00", out var result);

        succeeded.Should().BeTrue();
        result.Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void TryParseFormTime_BareDigitsOver24_RejectedNotMisreadAsMultiDay()
    {
        // The bug this guards: TimeSpan.TryParse("93", ...) succeeds as a 93-day span
        // (days.hh:mm shorthand), which would otherwise silently place a block 93 days
        // out with nothing to catch it.
        var succeeded = PlannerPageViewModel.TryParseFormTime("93", out var result);

        succeeded.Should().BeFalse();
        result.Should().Be(default(TimeSpan));
    }

    [Fact]
    public void TryParseFormTime_ExplicitDayPrefix_Rejected()
    {
        var succeeded = PlannerPageViewModel.TryParseFormTime("1.09:00", out _);

        succeeded.Should().BeFalse();
    }

    [Fact]
    public void TryParseFormTime_Garbage_Rejected()
    {
        var succeeded = PlannerPageViewModel.TryParseFormTime("not a time", out _);

        succeeded.Should().BeFalse();
    }

    [Fact]
    public void TryParseFormTime_ColonDigitDigit_KeepsExistingPositionalBehavior()
    {
        // "9:3" deliberately keeps parsing as 09:03, not 09:30 — a full custom positional
        // parser was considered and explicitly rejected as overkill for this rare edge case.
        var succeeded = PlannerPageViewModel.TryParseFormTime("9:3", out var result);

        succeeded.Should().BeTrue();
        result.Should().Be(new TimeSpan(9, 3, 0));
    }
}
