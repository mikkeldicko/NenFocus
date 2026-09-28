using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class ContractDayTests
{
    private static readonly DateOnly Monday = new(2026, 7, 6);
    private static DateTime At(int hour, int minute = 0) => Monday.ToDateTime(new TimeOnly(hour, minute));
    private static readonly DateTime NextMidnight = Monday.AddDays(1).ToDateTime(TimeOnly.MinValue);

    [Fact]
    public void Constructor_ValidDay_KeepsItsFacts()
    {
        var day = new ContractDay(Monday, At(9), At(17), new DailyFloor(4));

        day.Date.Should().Be(Monday);
        day.WindowStart.Should().Be(At(9));
        day.WindowEnd.Should().Be(At(17));
        day.Floor.Should().Be(new DailyFloor(4));
    }

    [Fact]
    public void Constructor_WindowEndingAtNextMidnight_IsAllowed()
    {
        var day = new ContractDay(Monday, At(0), NextMidnight, null);

        day.WindowEnd.Should().Be(NextMidnight);
    }

    [Fact]
    public void Constructor_WindowStartOnAnotherDate_Throws()
    {
        var act = () => new ContractDay(Monday, At(9).AddDays(-1), At(17), null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WindowEndNotAfterStart_Throws()
    {
        var act = () => new ContractDay(Monday, At(12), At(12), null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WindowEndPastNextMidnight_Throws()
    {
        var act = () => new ContractDay(Monday, At(9), NextMidnight.AddMinutes(1), null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_FloorLongerThanWindow_Throws()
    {
        // 6h floor on a 12–17 day (5h) can never be met.
        var act = () => new ContractDay(Monday, At(12), At(17), new DailyFloor(6));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_FloorExactlyTheWindowLength_IsAllowed()
    {
        var day = new ContractDay(Monday, At(12), At(17), new DailyFloor(5));

        day.Floor!.Hours.Should().Be(5);
    }

    [Fact]
    public void IsRestDay_NoFloor_IsTrue()
    {
        new ContractDay(Monday, At(9), At(17), null).IsRestDay.Should().BeTrue();
        new ContractDay(Monday, At(9), At(17), new DailyFloor(2)).IsRestDay.Should().BeFalse();
    }

    [Theory]
    [InlineData(9, 0, 17, 0, true)]    // exactly the window
    [InlineData(10, 0, 12, 0, true)]   // inside
    [InlineData(8, 59, 12, 0, false)]  // starts before the window
    [InlineData(16, 0, 17, 1, false)]  // ends after the window
    public void Contains_ChecksTheRangeAgainstTheWindow(int sh, int sm, int eh, int em, bool expected)
    {
        var day = new ContractDay(Monday, At(9), At(17), null);

        day.Contains(At(sh, sm), At(eh, em)).Should().Be(expected);
    }

    [Fact]
    public void Contains_RangeEndingAtNextMidnight_FitsAFullDayWindow()
    {
        var day = new ContractDay(Monday, At(0), NextMidnight, null);

        day.Contains(At(19), NextMidnight).Should().BeTrue();
    }
}
