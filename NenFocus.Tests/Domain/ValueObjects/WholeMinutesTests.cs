using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

/// <summary>
/// Focus is counted in whole minutes (Gap 92, V10): a fraction can't be built, and every way from a
/// TimeSpan says which way it rounds — or that it must already be exact.
/// </summary>
public class WholeMinutesTests
{
    [Fact]
    public void Of_ANegativeCount_Throws()
    {
        var act = () => WholeMinutes.Of(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Zero_IsTheDefault()
        => default(WholeMinutes).Should().Be(WholeMinutes.Zero);

    [Theory]
    [InlineData(31, 54, 31)]
    [InlineData(31, 0, 31)]
    [InlineData(0, 59, 0)]
    public void RoundDown_DropsThePartialMinute(int minutes, int seconds, long expected)
        => WholeMinutes.RoundDown(new TimeSpan(0, minutes, seconds)).Should().Be(WholeMinutes.Of(expected));

    [Fact]
    public void RoundDown_ANegativeSpan_IsZero()
        => WholeMinutes.RoundDown(TimeSpan.FromMinutes(-3)).Should().Be(WholeMinutes.Zero);

    [Fact]
    public void TryExact_AWholeSpan_IsThatMany()
        => WholeMinutes.TryExact(TimeSpan.FromMinutes(30)).Should().Be(WholeMinutes.Of(30));

    [Theory]
    [InlineData(30, 30)]                // half a minute
    [InlineData(0, 0, 1)]               // a single millisecond
    public void TryExact_AnythingFiner_IsNothing(int minutes, int seconds, int milliseconds = 0)
        => WholeMinutes.TryExact(new TimeSpan(0, 0, minutes, seconds, milliseconds)).Should().BeNull();

    [Fact]
    public void Arithmetic_IsExact()
    {
        var a = WholeMinutes.Of(29);
        var b = WholeMinutes.Of(31);

        (a + b).Should().Be(WholeMinutes.Of(60));
        (b - a).Should().Be(WholeMinutes.Of(2));
        (a + b).AsTimeSpan.Should().Be(TimeSpan.FromHours(1));
        WholeMinutes.Min(a, b).Should().Be(a);
        (a < b).Should().BeTrue();
    }

    [Fact]
    public void Subtracting_MoreThanThereIs_Throws()
    {
        var act = () => WholeMinutes.Of(1) - WholeMinutes.Of(2);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Less_StopsAtZero()
        => WholeMinutes.Of(1).Less(WholeMinutes.Of(2)).Should().Be(WholeMinutes.Zero);
}
