using NenFocus.Infrastructure.Services;
using Xunit;

namespace NenFocus.Tests.Infrastructure.Services;

public class SystemClockTests
{
    [Fact]
    public void UtcNow_WithNoOverride_TracksRealTime()
    {
        using var clock = new SystemClock();

        var before = DateTime.UtcNow;
        var reported = clock.UtcNow;
        var after = DateTime.UtcNow;

        Assert.False(clock.IsOverridden);
        Assert.InRange(reported, before, after.AddSeconds(1));
    }

    [Fact]
    public void SetTo_MakesUtcNowReflectTheGivenMomentAndKeepsAdvancing()
    {
        using var clock = new SystemClock();
        var target = DateTime.UtcNow.AddDays(3);

        clock.SetTo(target);

        Assert.True(clock.IsOverridden);
        Assert.InRange(clock.UtcNow, target, target.AddSeconds(1));
    }

    [Fact]
    public void AdvanceBy_AddsToWhateverOffsetAlreadyExists()
    {
        using var clock = new SystemClock();
        var realNow = DateTime.UtcNow;

        clock.AdvanceBy(TimeSpan.FromHours(1));
        clock.AdvanceBy(TimeSpan.FromHours(2));

        Assert.InRange(clock.UtcNow, realNow.AddHours(3), realNow.AddHours(3).AddSeconds(1));
    }

    [Fact]
    public void Reset_ReturnsToRealTime()
    {
        using var clock = new SystemClock();
        clock.SetTo(DateTime.UtcNow.AddDays(1));

        var before = DateTime.UtcNow;
        clock.Reset();
        var after = DateTime.UtcNow;

        Assert.False(clock.IsOverridden);
        Assert.InRange(clock.UtcNow, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public void SetTo_RaisesChanged()
    {
        using var clock = new SystemClock();
        var raised = false;
        clock.Changed += (_, _) => raised = true;

        clock.SetTo(DateTime.UtcNow.AddHours(5));

        Assert.True(raised);
    }

    [Fact]
    public void ToLocal_PreservesTheInstantAndReportsLocalKind()
    {
        using var clock = new SystemClock();
        var utc = DateTime.UtcNow;

        var local = clock.ToLocal(utc);

        Assert.Equal(DateTimeKind.Local, local.Kind);
        Assert.Equal(utc, local.ToUniversalTime());
    }
}
