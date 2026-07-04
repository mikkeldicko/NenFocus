using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class BlockedSiteTests
{
    [Fact]
    public void Constructor_TrimsAndLowercasesUrl()
    {
        var site = new BlockedSite("  Facebook.com  ");

        site.Url.Should().Be("facebook.com");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankUrl_Throws(string? blank)
    {
        var act = () => new BlockedSite(blank!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToString_ReturnsUrl()
    {
        var site = new BlockedSite("facebook.com");

        site.ToString().Should().Be("facebook.com");
    }
}
