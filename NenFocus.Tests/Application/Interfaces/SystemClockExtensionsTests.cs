using FluentAssertions;
using NenFocus.Application.Interfaces;
using NSubstitute;

namespace NenFocus.Tests.Application.Interfaces;

public class SystemClockExtensionsTests
{
    // Fixed non-UTC offset (UTC-5) deliberately chosen close to midnight, mirroring the
    // real-world case this seam exists to make testable: a call site that forgets to
    // convert would read the wrong calendar day here, where a naive same-offset test
    // wouldn't catch it.
    [Fact]
    public void ToLocalDate_UsesTheClocksConversion_NotTheRawUtcDate()
    {
        var clock = Substitute.For<ISystemClock>();
        var utc = new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Utc); // 1:00 AM UTC, Jan 2
        var local = new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Local); // 8:00 PM local, Jan 1 (UTC-5)
        clock.ToLocal(utc).Returns(local);

        var result = clock.ToLocalDate(utc);

        result.Should().Be(new DateOnly(2026, 1, 1));
        result.Should().NotBe(DateOnly.FromDateTime(utc));
    }
}
