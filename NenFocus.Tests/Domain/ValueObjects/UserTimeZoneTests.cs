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
