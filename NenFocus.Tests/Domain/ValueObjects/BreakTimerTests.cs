using FluentAssertions;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class BreakTimerTests
{
    private static readonly BreakTimer Classic = new(25, 5, 4, 15);

    private static SessionBreak Short(int startMinute, int endMinute) =>
        new(BreakKind.ShortBreak, TimeSpan.FromMinutes(startMinute), TimeSpan.FromMinutes(endMinute));

    private static SessionBreak Long(int startMinute, int endMinute) =>
        new(BreakKind.LongBreak, TimeSpan.FromMinutes(startMinute), TimeSpan.FromMinutes(endMinute));

    [Theory]
    [InlineData(0, 5, 4, 15, "workMinutes")]
    [InlineData(25, 0, 4, 15, "shortBreakMinutes")]
    [InlineData(25, 5, 0, 15, "cyclesBeforeLongBreak")]
    [InlineData(25, 5, 4, 0, "longBreakMinutes")]
    public void Constructor_ZeroValue_Throws(int work, int shortBreak, int cycles, int longBreak, string parameter)
    {
        var act = () => new BreakTimer(work, shortBreak, cycles, longBreak);

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be(parameter);
    }

    [Fact]
    public void ScheduleFor_TwoHours_Classic_ProducesShortBreaksThenLongBreak()
    {
        var schedule = Classic.ScheduleFor(TimeSpan.FromHours(2));

        // W 0-25, S 25-30, W 30-55, S 55-60, W 60-85, S 85-90, W 90-115, then the
        // 15-minute long break (115-130) would end after the 120-minute session — skipped.
        schedule.Breaks.Should().Equal(Short(25, 30), Short(55, 60), Short(85, 90));
    }

    [Fact]
    public void ScheduleFor_LongEnough_InsertsLongBreakAfterConfiguredCycles_ThenRestartsCadence()
    {
        var schedule = Classic.ScheduleFor(TimeSpan.FromMinutes(180));

        schedule.Breaks.Should().Equal(
            Short(25, 30), Short(55, 60), Short(85, 90),
            Long(115, 130),
            Short(155, 160));
    }

    [Fact]
    public void ScheduleFor_BreakThatWouldEndExactlyAtSessionEnd_IsSkipped()
    {
        // 30 minutes: the 25-30 break would end exactly when the session does — no work after it.
        var schedule = Classic.ScheduleFor(TimeSpan.FromMinutes(30));

        schedule.Breaks.Should().BeEmpty();
    }

    [Fact]
    public void ScheduleFor_BreakWithWorkTimeAfterIt_IsIncluded()
    {
        var schedule = Classic.ScheduleFor(TimeSpan.FromMinutes(31));

        schedule.Breaks.Should().Equal(Short(25, 30));
    }

    [Fact]
    public void ScheduleFor_SessionShorterThanOneWorkStretch_HasNoBreaks()
    {
        var schedule = Classic.ScheduleFor(TimeSpan.FromMinutes(20));

        schedule.Breaks.Should().BeEmpty();
    }

    [Fact]
    public void ScheduleFor_NonPositiveLength_HasNoBreaks()
    {
        Classic.ScheduleFor(TimeSpan.Zero).Breaks.Should().BeEmpty();
    }

    [Fact]
    public void ScheduleFor_AlwaysFitsWithinTheLengthItWasBuiltFor()
    {
        foreach (var minutes in new[] { 1, 26, 31, 59, 61, 120, 131, 480 })
        {
            var length = TimeSpan.FromMinutes(minutes);
            Classic.ScheduleFor(length).FitsWithin(length).Should().BeTrue($"{minutes} minutes");
        }
    }
}
