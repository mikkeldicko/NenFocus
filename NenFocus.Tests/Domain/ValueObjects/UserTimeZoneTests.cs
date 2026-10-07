using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

/// <summary>
/// Copenhagen: UTC+1 in winter, UTC+2 in summer. In 2026 clocks go forward on 29 March
/// (02:00 → 03:00, so 02:30 never happens) and back on 25 October (03:00 → 02:00, so 02:30 happens twice).
/// </summary>
public class UserTimeZoneTests
{
    private static readonly UserTimeZone Copenhagen = new(TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen"));

    // ── SlotCovering: the real time a calendar day covers (Gap 97) ───────────

    [Fact]
    public void SlotCovering_ADay_IsLocalMidnightToTheNextLocalMidnight()
        => Copenhagen.SlotCovering(new DateOnly(2026, 1, 15))
            .Should().Be(new TimeSlot(At.Utc(2026, 1, 14, 23), At.Utc(2026, 1, 15, 23)));

    [Fact]
    public void SlotCovering_TheDayClocksGoForward_Is23Hours()
        => Copenhagen.SlotCovering(new DateOnly(2026, 3, 29)).Length.Should().Be(TimeSpan.FromHours(23));

    [Fact]
    public void SlotCovering_TheDayClocksGoBack_Is25Hours()
        => Copenhagen.SlotCovering(new DateOnly(2026, 10, 25)).Length.Should().Be(TimeSpan.FromHours(25));

    [Fact]
    public void SlotCovering_ARangeOfDays_RunsFromTheFirstMidnightToTheMidnightAfterTheLast()
        => Copenhagen.SlotCovering(new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 17))
            .Should().Be(new TimeSlot(At.Utc(2026, 1, 14, 23), At.Utc(2026, 1, 17, 23)));

    [Fact]
    public void SlotCovering_IsTheMirrorOfSplitByDay()
    {
        var days = Copenhagen.SplitByDay(Copenhagen.SlotCovering(new DateOnly(2026, 3, 28), new DateOnly(2026, 3, 30))).ToList();

        days.Select(d => d.Date).Should().Equal(new DateOnly(2026, 3, 28), new DateOnly(2026, 3, 29), new DateOnly(2026, 3, 30));
        days.Should().OnlyContain(d => d.Part == Copenhagen.SlotCovering(d.Date));
    }

    [Fact]
    public void SlotCovering_LastDayBeforeTheFirst_Throws()
    {
        var act = () => Copenhagen.SlotCovering(new DateOnly(2026, 1, 17), new DateOnly(2026, 1, 15));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToCalendarTime_Winter_AddsOneHour()
        => Copenhagen.ToCalendarTime(At.Utc(2026, 1, 15, 12)).Should().Be(new DateTime(2026, 1, 15, 13, 0, 0));

    [Fact]
    public void ToCalendarTime_Summer_AddsTwoHours()
        => Copenhagen.ToCalendarTime(At.Utc(2026, 7, 15, 12)).Should().Be(new DateTime(2026, 7, 15, 14, 0, 0));

    [Fact]
    public void ToCalendarTime_IsACalendarTimeNotAMoment()
        => Copenhagen.ToCalendarTime(At.Utc(2026, 7, 15, 12)).Kind.Should().Be(DateTimeKind.Unspecified);

    [Fact]
    public void ToCalendarTime_BothPassesThroughTheRepeatedHour_ShowTheSameClockTime()
    {
        Copenhagen.ToCalendarTime(At.Utc(2026, 10, 25, 0, 30)).Should().Be(new DateTime(2026, 10, 25, 2, 30, 0));
        Copenhagen.ToCalendarTime(At.Utc(2026, 10, 25, 1, 30)).Should().Be(new DateTime(2026, 10, 25, 2, 30, 0));
    }

    [Fact]
    public void ToMoment_Winter_SubtractsOneHour()
        => Copenhagen.ToMoment(new DateTime(2026, 1, 15, 13, 0, 0)).Should().Be(At.Utc(2026, 1, 15, 12));

    [Fact]
    public void ToMoment_Summer_SubtractsTwoHours()
        => Copenhagen.ToMoment(new DateTime(2026, 7, 15, 14, 0, 0)).Should().Be(At.Utc(2026, 7, 15, 12));

    [Fact]
    public void ToMoment_IgnoresTheKindItIsGiven()
        => Copenhagen.ToMoment(new DateTime(2026, 7, 15, 14, 0, 0, DateTimeKind.Utc)).Should().Be(At.Utc(2026, 7, 15, 12));

    [Fact]
    public void ToMoment_SkippedSpringTime_UsesWinterOffset()
        // 02:30 doesn't exist on 29 March; today's ToUtc reads it with the winter offset, never throws.
        => Copenhagen.ToMoment(new DateTime(2026, 3, 29, 2, 30, 0)).Should().Be(At.Utc(2026, 3, 29, 1, 30));

    [Fact]
    public void ToMoment_RepeatedAutumnTime_UsesWinterOffset()
        // 02:30 happens twice on 25 October; today's ToUtc picks the second (winter) one.
        => Copenhagen.ToMoment(new DateTime(2026, 10, 25, 2, 30, 0)).Should().Be(At.Utc(2026, 10, 25, 1, 30));

    [Fact]
    public void DateOf_LateEveningUtc_IsTheNextDayInCopenhagen()
        => Copenhagen.DateOf(At.Utc(2026, 9, 29, 23, 30)).Should().Be(new DateOnly(2026, 9, 30));

    [Fact]
    public void DateOf_SameDay_WhenUtcAndLocalAgree()
        => Copenhagen.DateOf(At.Utc(2026, 9, 29, 12)).Should().Be(new DateOnly(2026, 9, 29));

    [Fact]
    public void ToCalendarTimes_ShowsBothEndsOfASlotOnTheUsersClock()
    {
        var slot = new TimeSlot(At.Utc(2026, 7, 15, 8), At.Utc(2026, 7, 15, 9, 30));

        Copenhagen.ToCalendarTimes(slot).Should().Be((new DateTime(2026, 7, 15, 10, 0, 0), new DateTime(2026, 7, 15, 11, 30, 0)));
    }

    // SplitByDay — cut at the user's local midnights, in real time (Gap 88 phase 4; replaces
    // DayRangeSplitter, which cut calendar times and so got DST nights an hour wrong).

    private static TimeSlot LocalSlot(DateTime localStart, DateTime localEnd)
        => new(Copenhagen.ToMoment(localStart), Copenhagen.ToMoment(localEnd));

    [Fact]
    public void SplitByDay_WithinOneDay_IsOnePart()
    {
        var slot = LocalSlot(new DateTime(2026, 1, 15, 9, 0, 0), new DateTime(2026, 1, 15, 11, 0, 0));

        Copenhagen.SplitByDay(slot).Should().Equal((new DateOnly(2026, 1, 15), slot));
    }

    [Fact]
    public void SplitByDay_AcrossLocalMidnight_CutsAtLocalMidnightNotUtcMidnight()
    {
        var slot = LocalSlot(new DateTime(2026, 1, 15, 23, 0, 0), new DateTime(2026, 1, 16, 1, 0, 0));
        var localMidnight = Copenhagen.ToMoment(new DateTime(2026, 1, 16, 0, 0, 0)); // 23:00 UTC

        Copenhagen.SplitByDay(slot).Should().Equal(
            (new DateOnly(2026, 1, 15), new TimeSlot(Copenhagen.ToMoment(new DateTime(2026, 1, 15, 23, 0, 0)), localMidnight)),
            (new DateOnly(2026, 1, 16), new TimeSlot(localMidnight, Copenhagen.ToMoment(new DateTime(2026, 1, 16, 1, 0, 0)))));
    }

    [Fact]
    public void SplitByDay_JustAfterLocalMidnight_BelongsToTheLocalDay()
    {
        // 00:30–01:30 in Copenhagen is 23:30–00:30 UTC the day before: it's the local day that counts.
        var slot = LocalSlot(new DateTime(2026, 1, 16, 0, 30, 0), new DateTime(2026, 1, 16, 1, 30, 0));

        Copenhagen.SplitByDay(slot).Select(p => p.Date).Should().Equal(new DateOnly(2026, 1, 16));
    }

    [Fact]
    public void SplitByDay_SeveralDays_OnePartPerDayTouched()
    {
        var slot = LocalSlot(new DateTime(2026, 1, 1, 22, 0, 0), new DateTime(2026, 1, 3, 2, 0, 0));

        var parts = Copenhagen.SplitByDay(slot).ToList();

        parts.Select(p => p.Date).Should().Equal(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 3));
        parts.Select(p => p.Part.Length).Should().Equal(TimeSpan.FromHours(2), TimeSpan.FromHours(24), TimeSpan.FromHours(2));
    }

    [Fact]
    public void SplitByDay_EndingExactlyAtLocalMidnight_HasNoPresenceOnTheNextDay()
    {
        var slot = LocalSlot(new DateTime(2026, 1, 1, 15, 0, 0), new DateTime(2026, 1, 2, 0, 0, 0));

        Copenhagen.SplitByDay(slot).Select(p => p.Date).Should().Equal(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void SplitByDay_SpringForwardNight_TheDayIsOnly23HoursLong()
    {
        // 29 March 2026: 02:00 jumps to 03:00. Midnight to midnight is 23 real hours.
        var slot = LocalSlot(new DateTime(2026, 3, 29, 0, 0, 0), new DateTime(2026, 3, 30, 0, 0, 0));

        Copenhagen.SplitByDay(slot).Should().ContainSingle().Which.Part.Length.Should().Be(TimeSpan.FromHours(23));
    }

    [Fact]
    public void SplitByDay_FallBackNight_TheDayIs25HoursLong()
    {
        // 25 October 2026: 03:00 falls back to 02:00. Midnight to midnight is 25 real hours.
        var slot = LocalSlot(new DateTime(2026, 10, 25, 0, 0, 0), new DateTime(2026, 10, 26, 0, 0, 0));

        Copenhagen.SplitByDay(slot).Should().ContainSingle().Which.Part.Length.Should().Be(TimeSpan.FromHours(25));
    }

    // HoursPerDay — how many hours of the given slots land on each local day (Gap 92 S4; moved
    // here from PlannedFocusHoursCalculator, which was only this sum over SplitByDay).

    private static IReadOnlyDictionary<DateOnly, double> HoursPerDay(params TimeSlot[] slots) => Copenhagen.HoursPerDay(slots);

    [Fact]
    public void HoursPerDay_NoSlots_IsEmpty()
        => HoursPerDay().Should().BeEmpty();

    [Fact]
    public void HoursPerDay_OneSlotWithinADay_CountsOnThatDay()
        => HoursPerDay(LocalSlot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 11, 0, 0)))
            .Should().ContainKey(new DateOnly(2026, 1, 1)).WhoseValue.Should().Be(2);

    [Fact]
    public void HoursPerDay_SlotsOnTheSameDay_AddUp()
    {
        var result = HoursPerDay(
            LocalSlot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0)),
            LocalSlot(new DateTime(2026, 1, 1, 14, 0, 0), new DateTime(2026, 1, 1, 16, 0, 0)));

        result[new DateOnly(2026, 1, 1)].Should().Be(3);
    }

    [Fact]
    public void HoursPerDay_SlotsOnDifferentDays_CountSeparately()
    {
        var result = HoursPerDay(
            LocalSlot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0)),
            LocalSlot(new DateTime(2026, 1, 2, 9, 0, 0), new DateTime(2026, 1, 2, 12, 0, 0)));

        result[new DateOnly(2026, 1, 1)].Should().Be(1);
        result[new DateOnly(2026, 1, 2)].Should().Be(3);
    }

    [Fact]
    public void HoursPerDay_SlotCrossingMidnight_GivesEachDayItsShare()
    {
        var result = HoursPerDay(LocalSlot(new DateTime(2026, 1, 1, 23, 0, 0), new DateTime(2026, 1, 2, 1, 0, 0)));

        result[new DateOnly(2026, 1, 1)].Should().Be(1);
        result[new DateOnly(2026, 1, 2)].Should().Be(1);
    }

    [Fact]
    public void HoursPerDay_SlotSpanningSeveralDays_CountsEachDay()
    {
        var result = HoursPerDay(LocalSlot(new DateTime(2026, 1, 1, 22, 0, 0), new DateTime(2026, 1, 3, 2, 0, 0)));

        result[new DateOnly(2026, 1, 1)].Should().Be(2);   // 22:00-24:00
        result[new DateOnly(2026, 1, 2)].Should().Be(24);  // full day
        result[new DateOnly(2026, 1, 3)].Should().Be(2);   // 00:00-02:00
    }

    [Fact]
    public void HoursPerDay_JustAfterLocalMidnight_CountsOnTheLocalDay()
        // 00:30–01:30 local is the previous day in UTC; the user's day is what counts.
        => HoursPerDay(LocalSlot(new DateTime(2026, 1, 2, 0, 30, 0), new DateTime(2026, 1, 2, 1, 30, 0)))
            .Should().Equal(new Dictionary<DateOnly, double> { [new DateOnly(2026, 1, 2)] = 1 });

    [Fact]
    public void HoursPerDay_SpringForwardNight_CountsTheRealHours()
        // 01:00–04:00 on 29 March 2026 reads as 3 hours but lasts 2: 02:00 jumps to 03:00.
        => HoursPerDay(LocalSlot(new DateTime(2026, 3, 29, 1, 0, 0), new DateTime(2026, 3, 29, 4, 0, 0)))
            [new DateOnly(2026, 3, 29)].Should().Be(2);

    [Fact]
    public void HoursPerDay_FallBackNight_CountsTheRealHours()
        // 01:00–04:00 on 25 October 2026 reads as 3 hours but lasts 4: 03:00 falls back to 02:00.
        => HoursPerDay(LocalSlot(new DateTime(2026, 10, 25, 1, 0, 0), new DateTime(2026, 10, 25, 4, 0, 0)))
            [new DateOnly(2026, 10, 25)].Should().Be(4);

    [Fact]
    public void MachineZone_MatchesTodaysConversions_EveryQuarterHourOfTheYear()
    {
        // Pins "same behaviour as today" (Gap 88 phase 1): UserTimeZone over the machine's own zone
        // must agree with SystemClock's ToLocal (ToLocalTime) and ToUtc (SpecifyKind Local →
        // ToUniversalTime) at every quarter hour of 2026, both DST nights included.
        var zone = new UserTimeZone(TimeZoneInfo.Local);
        var mismatches = new List<string>();
        for (var t = new DateTime(2026, 1, 1, 0, 0, 0); t < new DateTime(2027, 1, 1); t = t.AddMinutes(15))
        {
            var utc = DateTime.SpecifyKind(t, DateTimeKind.Utc);
            if (zone.ToCalendarTime(Moment.FromStorage(utc)) != utc.ToLocalTime())
                mismatches.Add($"calendar time of {utc:s}Z");

            if (zone.ToMoment(t).ToStorage() != DateTime.SpecifyKind(t, DateTimeKind.Local).ToUniversalTime())
                mismatches.Add($"moment of calendar time {t:s}");
        }

        mismatches.Should().BeEmpty();
    }
}
