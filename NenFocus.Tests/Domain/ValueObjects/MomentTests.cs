using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class MomentTests
{
    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void FromStorage_NonUtcValue_Throws(DateTimeKind kind)
    {
        var act = () => Moment.FromStorage(new DateTime(2026, 9, 29, 13, 0, 0, kind));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToStorage_ReturnsTheSameUtcValue()
    {
        var utc = new DateTime(2026, 9, 29, 13, 0, 0, DateTimeKind.Utc);

        var stored = Moment.FromStorage(utc).ToStorage();

        stored.Should().Be(utc);
        stored.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Default_IsStoredAsUtc()
        => default(Moment).ToStorage().Kind.Should().Be(DateTimeKind.Utc);

    [Fact]
    public void SameInstant_IsEqual()
        => At.Utc(2026, 9, 29, 13).Should().Be(At.Utc(2026, 9, 29, 13));

    [Fact]
    public void ComparisonOperators_OrderByInstant()
    {
        var earlier = At.Utc(2026, 9, 29, 13);
        var later = At.Utc(2026, 9, 29, 14);

        (earlier < later).Should().BeTrue();
        (earlier <= later).Should().BeTrue();
        (later > earlier).Should().BeTrue();
        (later >= earlier).Should().BeTrue();
        (earlier <= At.Utc(2026, 9, 29, 13)).Should().BeTrue();
        (earlier > later).Should().BeFalse();
    }

    [Fact]
    public void CompareTo_SortsByInstant()
    {
        var moments = new[] { At.Utc(2026, 9, 29, 15), At.Utc(2026, 9, 29, 13), At.Utc(2026, 9, 29, 14) };

        moments.Order().Should().Equal(At.Utc(2026, 9, 29, 13), At.Utc(2026, 9, 29, 14), At.Utc(2026, 9, 29, 15));
    }

    [Fact]
    public void AddingAndSubtractingATimeSpan_MovesTheMoment()
    {
        var moment = At.Utc(2026, 9, 29, 13);

        (moment + TimeSpan.FromMinutes(30)).Should().Be(At.Utc(2026, 9, 29, 13, 30));
        (moment - TimeSpan.FromHours(1)).Should().Be(At.Utc(2026, 9, 29, 12));
    }

    [Fact]
    public void SubtractingTwoMoments_GivesTheDistanceBetweenThem()
        => (At.Utc(2026, 9, 29, 14, 30) - At.Utc(2026, 9, 29, 13)).Should().Be(TimeSpan.FromMinutes(90));

    [Fact]
    public void HasNoCalendarMembers()
    {
        // The point of the type (Gaps 65/87/96): "which day / what hour" needs a time zone, so it
        // goes through UserTimeZone — a Moment can't answer it by accident.
        string[] calendarMembers = ["Date", "Day", "Month", "Year", "Hour", "Minute", "TimeOfDay", "DayOfWeek", "DayOfYear"];

        typeof(Moment).GetMembers().Select(m => m.Name).Should().NotContain(calendarMembers);
    }
}
