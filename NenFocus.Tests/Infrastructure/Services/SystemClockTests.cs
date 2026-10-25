using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Services;
using Xunit;

namespace NenFocus.Tests.Infrastructure.Services;

public class SystemClockTests
{
    [Fact]
    public void Now_WithNoOverride_TracksRealTime()
    {
        var clock = new SystemClock();

        var before = DateTime.UtcNow;
        var reported = clock.Now.ToStorage();
        var after = DateTime.UtcNow;

        Assert.False(clock.IsOverridden);
        Assert.InRange(reported, before, after.AddSeconds(1));
    }

    [Fact]
    public void SetTo_MakesNowReflectTheGivenMomentAndKeepsAdvancing()
    {
        var clock = new SystemClock();
        var target = DateTime.UtcNow.AddDays(3);

        clock.SetTo(target);

        Assert.True(clock.IsOverridden);
        Assert.InRange(clock.Now.ToStorage(), target, target.AddSeconds(1));
    }

    [Fact]
    public void AdvanceBy_AddsToWhateverOffsetAlreadyExists()
    {
        var clock = new SystemClock();
        var realNow = DateTime.UtcNow;

        clock.AdvanceBy(TimeSpan.FromHours(1));
        clock.AdvanceBy(TimeSpan.FromHours(2));

        Assert.InRange(clock.Now.ToStorage(), realNow.AddHours(3), realNow.AddHours(3).AddSeconds(1));
    }

    [Fact]
    public void Reset_ReturnsToRealTime()
    {
        var clock = new SystemClock();
        clock.SetTo(DateTime.UtcNow.AddDays(1));

        var before = DateTime.UtcNow;
        clock.Reset();
        var after = DateTime.UtcNow;

        Assert.False(clock.IsOverridden);
        Assert.InRange(clock.Now.ToStorage(), before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public void Now_CarriesTheDebugOffset()
    {
        // Gap 88: the Moment door must carry the same debug jump as the raw clock.
        var clock = new SystemClock();
        clock.AdvanceBy(TimeSpan.FromDays(2));

        var target = DateTime.UtcNow.AddDays(2);
        Assert.InRange(clock.Now.ToStorage(), target.AddSeconds(-1), target.AddSeconds(1));
    }

    [Fact]
    public void TimeZone_IsTheMachinesZone()
    {
        var clock = new SystemClock();

        Assert.Equal(new UserTimeZone(TimeZoneInfo.Local), clock.TimeZone);
    }

    // ── Jumped: only a jump of the debug clock, never a tick of its own (Gap 89 Slice A) ──

    [Fact]
    public void Jumped_IsRaisedBySetToAdvanceByAndReset()
    {
        var clock = new SystemClock();
        var jumps = 0;
        clock.Jumped += (_, _) => jumps++;

        clock.SetTo(DateTime.UtcNow.AddDays(1));
        clock.AdvanceBy(TimeSpan.FromHours(1));
        clock.Reset();

        Assert.Equal(3, jumps);
    }

    [Fact]
    public async Task Jumped_IsNotRaisedWhileTimeSimplyPasses()
    {
        // It used to fire every second from an ambient ticker kept for the debug overlay, which made
        // the Planner's minute clock tick every second too. The overlay's second-hand is IDisplayClock's now.
        var clock = new SystemClock();
        var jumps = 0;
        clock.Jumped += (_, _) => jumps++;

        await Task.Delay(TimeSpan.FromMilliseconds(1300));

        Assert.Equal(0, jumps);
    }
}
