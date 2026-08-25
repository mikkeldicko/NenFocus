using FluentAssertions;
using NenFocus.Converters;

namespace NenFocus.Tests.Converters;

public class DurationLabelConverterTests
{
    private readonly DurationLabelConverter _converter = new();

    [Theory]
    [InlineData(15, "(15 mins)")]
    [InlineData(30, "(30 mins)")]
    [InlineData(45, "(45 mins)")]
    public void Convert_UnderOneHour_FormatsAsMinutes(int minutes, string expected)
        => _converter.Convert(TimeSpan.FromMinutes(minutes), typeof(string), null, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(expected);

    [Fact]
    public void Convert_ExactlyOneHour_FormatsAsSingularHour()
        => _converter.Convert(TimeSpan.FromHours(1), typeof(string), null, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be("(1 hr)");

    [Theory]
    [InlineData(1.5, "(1.5 hrs)")]
    [InlineData(2, "(2 hrs)")]
    [InlineData(2.5, "(2.5 hrs)")]
    [InlineData(3, "(3 hrs)")]
    [InlineData(12, "(12 hrs)")]
    public void Convert_OverOneHour_FormatsAsDecimalHours(double hours, string expected)
        => _converter.Convert(TimeSpan.FromHours(hours), typeof(string), null, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(expected);

    [Fact]
    public void Convert_NonTimeSpanValue_ReturnsEmptyString()
        => _converter.Convert("not a timespan", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(string.Empty);

    [Fact]
    public void ConvertBack_Throws()
    {
        var act = () => _converter.ConvertBack("(1 hr)", typeof(TimeSpan), null, System.Globalization.CultureInfo.InvariantCulture);

        act.Should().Throw<NotSupportedException>();
    }
}
