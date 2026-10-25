using FluentAssertions;
using NenFocus.ViewModels.Planner;

namespace NenFocus.Tests.ViewModels.Planner;

public class PlannerTimeShiftMathTests
{
    private static TimeSpan At(int hour, int minute = 0) => new(hour, minute, 0);

    [Fact]
    public void EndRollsToNextDay_EndAfterStart_False()
        => PlannerTimeShiftMath.EndRollsToNextDay(At(10), At(11)).Should().BeFalse();

    [Fact]
    public void EndRollsToNextDay_EndEqualsStart_True()
        => PlannerTimeShiftMath.EndRollsToNextDay(At(10), At(10)).Should().BeTrue();

    [Fact]
    public void EndRollsToNextDay_EndBeforeStart_True()
        => PlannerTimeShiftMath.EndRollsToNextDay(At(23), At(1)).Should().BeTrue();

    [Fact]
    public void ResolvedDuration_SameDay_IsPlainDifference()
        => PlannerTimeShiftMath.ResolvedDuration(At(9), At(11, 30)).Should().Be(TimeSpan.FromHours(2.5));

    [Fact]
    public void ResolvedDuration_RollsToNextDay_AddsTwentyFourHours()
        // 23:00 to 01:00 — 2 hours, crossing midnight.
        => PlannerTimeShiftMath.ResolvedDuration(At(23), At(1)).Should().Be(TimeSpan.FromHours(2));

    [Fact]
    public void ResolvedDuration_EndEqualsStart_IsTwentyFourHours()
        => PlannerTimeShiftMath.ResolvedDuration(At(10), At(10)).Should().Be(TimeSpan.FromHours(24));

    [Fact]
    public void ResolvedDuration_JustBeforeMidnightToJustAfter_IsOneMinute()
        => PlannerTimeShiftMath.ResolvedDuration(At(23, 59), At(0, 0)).Should().Be(TimeSpan.FromMinutes(1));

    [Fact]
    public void ShiftEnd_StaysSameDay_WhenSumUnderTwentyFourHours()
        => PlannerTimeShiftMath.ShiftEnd(At(9), TimeSpan.FromHours(2)).Should().Be(At(11));

    [Fact]
    public void ShiftEnd_WrapsPastMidnight_WhenSumReachesTwentyFourHours()
        // Start shifts to 23:00, preserving a 2h duration — new End is 01:00 the next day.
        => PlannerTimeShiftMath.ShiftEnd(At(23), TimeSpan.FromHours(2)).Should().Be(At(1));

    [Fact]
    public void ShiftEnd_ExactlyReachesMidnight_ReturnsZero()
        => PlannerTimeShiftMath.ShiftEnd(At(22), TimeSpan.FromHours(2)).Should().Be(TimeSpan.Zero);

    [Fact]
    public void ShiftThenResolve_RoundTrips_PreservesDuration()
    {
        // Shifting Start to a new value and re-resolving the duration from the shifted
        // End must reproduce the original duration exactly — the round-trip the form
        // relies on for repeated Start edits to keep preserving the same span.
        var originalStart = At(10, 15);
        var originalEnd = At(23, 45);
        var duration = PlannerTimeShiftMath.ResolvedDuration(originalStart, originalEnd);

        var newStart = At(23, 0);
        var shiftedEnd = PlannerTimeShiftMath.ShiftEnd(newStart, duration);

        PlannerTimeShiftMath.ResolvedDuration(newStart, shiftedEnd).Should().Be(duration);
    }
}
