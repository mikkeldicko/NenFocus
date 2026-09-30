using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;
using NSubstitute;
using NSubstitute.Extensions;

namespace NenFocus.Tests;

/// <summary>
/// Sets a fake ISystemClock's "now" — the single door, since Gap 88 phase 6 removed the old
/// <c>UtcNow</c>. Several values return in sequence, as NSubstitute's Returns does. Unspecified
/// times are read as UTC (the database's convention).
/// </summary>
internal static class TestClock
{
    /// <summary>
    /// The only way to make a fake clock (ClockRuleTests bans a bare Substitute.For): its Now and
    /// TimeZone throw until the test sets them, so a forgotten setup fails loudly instead of quietly
    /// running at year 0001 (Gap 88 close-out, lessons 1 and 12).
    /// </summary>
    public static ISystemClock Create()
    {
        var clock = Substitute.For<ISystemClock>();
        clock.Now.Returns(_ => throw new InvalidOperationException("The fake clock's Now wasn't set — call SetNow."));
        clock.TimeZone.Returns(_ => throw new InvalidOperationException("The fake clock's TimeZone wasn't set — call SetTimeZoneOffset."));
        return clock;
    }

    public static void SetNow(this ISystemClock clock, DateTime utc, params DateTime[] thenUtc)
    {
        // Configure() replaces the throwing default without running it (see Create).
        clock.Configure().Now.Returns(At.Utc(utc), thenUtc.Select(At.Utc).ToArray());
    }

    /// <summary>
    /// The fake's time zone as a fixed offset from UTC — the <c>TimeZone</c> twin of a test's
    /// <c>ToLocal(x) => x + offset</c> setup, so old and new doors agree (Gap 88; the ToLocal
    /// setups go in phase 6).
    /// </summary>
    public static void SetTimeZoneOffset(this ISystemClock clock, TimeSpan offset) =>
        clock.Configure().TimeZone.Returns(new UserTimeZone(
            TimeZoneInfo.CreateCustomTimeZone($"Test UTC {offset.TotalMinutes} min", offset, "Test", "Test")));
}
