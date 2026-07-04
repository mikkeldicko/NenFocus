using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class BlockedAppTests
{
    [Fact]
    public void Constructor_TrimsProcessName()
    {
        var app = new BlockedApp("  chrome  ");

        app.ProcessName.Should().Be("chrome");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankProcessName_Throws(string? blank)
    {
        var act = () => new BlockedApp(blank!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToString_ReturnsProcessName()
    {
        var app = new BlockedApp("chrome");

        app.ToString().Should().Be("chrome");
    }
}
