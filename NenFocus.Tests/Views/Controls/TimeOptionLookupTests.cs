using FluentAssertions;
using NenFocus.Views.Controls;

namespace NenFocus.Tests.Views.Controls;

public class TimeOptionLookupTests
{
    private static TimeSpan At(int hour, int minute = 0) => new(hour, minute, 0);

    // 15-minute grid, 00:00..23:45 — mirrors PlannerPageViewModel.GenerateTimeOptions's shape.
    private static readonly IReadOnlyList<TimeSpan> Grid = Enumerable.Range(0, 96)
        .Select(i => TimeSpan.FromMinutes(i * 15))
        .ToList();

    [Fact]
    public void TryGuessHour_NoDigits_ReturnsFalse()
        => TimeOptionLookup.TryGuessHour("abc", out _).Should().BeFalse();

    [Fact]
    public void TryGuessHour_SingleDigit_IsThatHour()
    {
        TimeOptionLookup.TryGuessHour("1", out var guess).Should().BeTrue();
        guess.Should().Be(At(1));
    }

    [Fact]
    public void TryGuessHour_TwoValidDigits_IsThatHour()
    {
        TimeOptionLookup.TryGuessHour("14", out var guess).Should().BeTrue();
        guess.Should().Be(At(14));
    }

    [Fact]
    public void TryGuessHour_ThirdDigitDoesNotChangeHourGuess()
    {
        TimeOptionLookup.TryGuessHour("144", out var guess).Should().BeTrue();
        guess.Should().Be(At(14));
    }

    [Fact]
    public void TryGuessHour_TwoDigitsExceeding23_FallsBackToFirstDigit()
    {
        TimeOptionLookup.TryGuessHour("99", out var guess).Should().BeTrue();
        guess.Should().Be(At(9));
    }

    [Fact]
    public void TryGuessHour_ColonIgnored_ReadsSurroundingDigits()
    {
        TimeOptionLookup.TryGuessHour("1:4", out var guess).Should().BeTrue();
        guess.Should().Be(At(14));
    }

    [Fact]
    public void NearestIndex_ExactMatch_ReturnsThatIndex()
        => TimeOptionLookup.NearestIndex(At(14), Grid).Should().Be(56); // 14h * 4 slots/hr

    [Fact]
    public void NearestIndex_OffGridCloserToLower_RoundsDown()
        => TimeOptionLookup.NearestIndex(At(14, 6), Grid).Should().Be(56); // 14:06 closer to 14:00

    [Fact]
    public void NearestIndex_OffGridCloserToUpper_RoundsUp()
        => TimeOptionLookup.NearestIndex(At(14, 9), Grid).Should().Be(57); // 14:09 closer to 14:15

    [Fact]
    public void IndexAfter_OffGridValue_LandsOnNextSlotForward()
        // The architect's own confirmed example: 14:07 + Down -> 14:15, not the nearer-but-backward 14:00.
        => Grid[TimeOptionLookup.IndexAfter(At(14, 7), Grid)].Should().Be(At(14, 15));

    [Fact]
    public void IndexAfter_OnGridValue_StepsToNextSlot()
        => Grid[TimeOptionLookup.IndexAfter(At(14, 0), Grid)].Should().Be(At(14, 15));

    [Fact]
    public void IndexAfter_AtLastSlot_ClampsInPlace()
        => TimeOptionLookup.IndexAfter(Grid[^1], Grid).Should().Be(Grid.Count - 1);

    [Fact]
    public void IndexBefore_OffGridValue_LandsOnPreviousSlotBackward()
        => Grid[TimeOptionLookup.IndexBefore(At(14, 7), Grid)].Should().Be(At(14, 0));

    [Fact]
    public void IndexBefore_OnGridValue_StepsToPreviousSlot()
        => Grid[TimeOptionLookup.IndexBefore(At(14, 15), Grid)].Should().Be(At(14, 0));

    [Fact]
    public void IndexBefore_AtFirstSlot_ClampsInPlace()
        => TimeOptionLookup.IndexBefore(Grid[0], Grid).Should().Be(0);
}
