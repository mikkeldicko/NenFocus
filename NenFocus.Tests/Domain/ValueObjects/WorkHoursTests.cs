using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class WorkHoursTests
{
    [Fact]
    public void Constructor_EndBeforeStart_Throws()
    {
        var act = () => new WorkHours(new TimeOnly(17, 0), new TimeOnly(9, 0));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_EndEqualsStart_Throws()
    {
        var act = () => new WorkHours(new TimeOnly(9, 0), new TimeOnly(9, 0));

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(2026, 1, 1, 9, 0, 2026, 1, 1, 17, 0, true)]   // exactly fills the window
    [InlineData(2026, 1, 1, 9, 0, 2026, 1, 1, 12, 0, true)]   // inside the window
    [InlineData(2026, 1, 1, 8, 59, 2026, 1, 1, 12, 0, false)] // starts before window opens
    [InlineData(2026, 1, 1, 9, 0, 2026, 1, 1, 17, 1, false)]  // ends after window closes
    [InlineData(2026, 1, 1, 17, 0, 2026, 1, 1, 18, 0, false)] // starts exactly at the window's close
    [InlineData(2026, 1, 1, 15, 0, 2026, 1, 2, 0, 0, false)]  // ends at midnight (next day 00:00) — must not wrap to 00:00 and read as "before" End
    public void ContainsRange_VariousBlocks(
        int sy, int sm, int sd, int sh, int smin,
        int ey, int em, int ed, int eh, int emin,
        bool expected)
    {
        var workHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0));
        var start = new DateTime(sy, sm, sd, sh, smin, 0);
        var end = new DateTime(ey, em, ed, eh, emin, 0);

        workHours.ContainsRange(start, end).Should().Be(expected);
    }

    [Fact]
    public void TryCreate_EndAfterStart_IsTheWorkHours()
        => WorkHours.TryCreate(new TimeOnly(9, 0), new TimeOnly(17, 0)).Should().Be(new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)));

    [Theory]
    [InlineData(17, 9)]
    [InlineData(9, 9)]
    public void TryCreate_EndNotAfterStart_IsNothing(int startHour, int endHour)
        => WorkHours.TryCreate(new TimeOnly(startHour, 0), new TimeOnly(endHour, 0)).Should().BeNull();

    [Fact]
    public void Length_IsTheWindow()
        => new WorkHours(new TimeOnly(9, 0), new TimeOnly(12, 30)).Length.Should().Be(TimeSpan.FromHours(3.5));
}
