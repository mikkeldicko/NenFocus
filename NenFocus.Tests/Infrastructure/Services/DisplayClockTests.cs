using NenFocus.Infrastructure.Services;
using Xunit;

namespace NenFocus.Tests.Infrastructure.Services;

/// <summary>The screen's paces (Gap 89 Slice A): a second for the debug overlay, a minute for the
/// Planner, and both at once when the debug clock jumps, so nothing waits for its next tick.</summary>
public class DisplayClockTests
{
    [Fact]
    public void ADebugClockJump_RaisesBothPacesAtOnce()
    {
        var systemClock = new SystemClock();
        using var display = new DisplayClock(systemClock);
        var seconds = 0;
        var minutes = 0;
        display.SecondTicked += (_, _) => seconds++;
        display.MinuteTicked += (_, _) => minutes++;

        systemClock.AdvanceBy(TimeSpan.FromHours(1));

        Assert.True(seconds >= 1);
        Assert.Equal(1, minutes);
    }

    [Fact]
    public async Task TheSecondPace_TicksEverySecond_TheMinutePaceDoesNot()
    {
        var systemClock = new SystemClock();
        using var display = new DisplayClock(systemClock);
        var seconds = 0;
        var minutes = 0;
        display.SecondTicked += (_, _) => seconds++;
        display.MinuteTicked += (_, _) => minutes++;

        await Task.Delay(TimeSpan.FromMilliseconds(2300));

        Assert.InRange(seconds, 1, 3);
        Assert.Equal(0, minutes);
    }
}
