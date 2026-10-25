using System.Globalization;
using FluentAssertions;
using NenFocus.Converters;

namespace NenFocus.Tests.Converters;

/// <summary>The one place a time of day becomes "HH:mm" text and back (Gap 89 Slice B). Its parsing
/// cases were PlannerPageViewModel.TryParseFormTime's, carried over unchanged.</summary>
public class TimeOfDayToStringConverterTests
{
    private readonly TimeOfDayToStringConverter _converter = new();

    private object? Convert(object? value) => _converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);
    private object? ConvertBack(string? text) => _converter.ConvertBack(text, typeof(TimeSpan?), null, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(10, 30, "10:30")]
    [InlineData(9, 5, "09:05")]
    [InlineData(0, 0, "00:00")]
    [InlineData(23, 59, "23:59")]
    public void Convert_ATimeOfDay_IsHoursAndMinutes(int hour, int minute, string expected)
        => Convert(new TimeSpan(hour, minute, 0)).Should().Be(expected);

    [Fact]
    public void Convert_TheEndOfTheDay_Is2400()
        => Convert(TimeSpan.FromHours(24)).Should().Be("24:00");

    [Fact]
    public void Convert_NoTime_IsEmpty()
        => Convert(null).Should().Be(string.Empty);

    [Theory]
    [InlineData("09:00", 9, 0)]
    [InlineData("9:05", 9, 5)]
    [InlineData("23:59", 23, 59)]
    [InlineData("00:00", 0, 0)]
    public void ConvertBack_AnOrdinaryTime_IsThatTime(string text, int hour, int minute)
        => ConvertBack(text).Should().Be(new TimeSpan(hour, minute, 0));

    [Fact]
    public void ConvertBack_2400_IsTwentyFourHours()
        => ConvertBack("24:00").Should().Be(TimeSpan.FromHours(24));

    [Theory]
    [InlineData("93")]          // TimeSpan.TryParse reads this as 93 days — a block 93 days out
    [InlineData("10")]          // likewise 10 days, not 10:00
    [InlineData("1.09:00")]     // an explicit day prefix
    [InlineData("25:00")]
    [InlineData("not a time")]
    [InlineData("")]
    [InlineData(null)]
    public void ConvertBack_AnythingElse_IsNoTime(string? text)
        => ConvertBack(text).Should().BeNull();

    [Fact]
    public void ConvertBack_ColonDigit_KeepsThePositionalReading()
        // "9:3" stays 09:03, not 09:30 — a positional parser was considered and rejected as overkill.
        => ConvertBack("9:3").Should().Be(new TimeSpan(9, 3, 0));

    [Theory]
    [InlineData("07:15")]
    [InlineData("24:00")]
    public void ARoundTrip_GivesTheSameText(string text)
        => Convert(ConvertBack(text)).Should().Be(text);
}
