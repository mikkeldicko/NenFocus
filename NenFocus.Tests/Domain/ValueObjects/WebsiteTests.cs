using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

/// <summary>
/// A website is a bare domain — the one definition of a site the blocklist, the slice, the hosts file
/// and the extension's check all share (Gap 112, I2). Allowed or blocked is which list it's in.
/// </summary>
public class WebsiteTests
{
    [Theory]
    [InlineData("  Facebook.com  ", "facebook.com")]
    [InlineData("https://www.YouTube.com/watch?v=1", "youtube.com")]
    [InlineData("http://example.com:8080/path", "example.com")]
    [InlineData("example.com#top", "example.com")]
    [InlineData("www.reddit.com", "reddit.com")]
    [InlineData("reddit.com.", "reddit.com")]
    [InlineData("mail.google.com", "mail.google.com")]
    [InlineData("://youtube.com", "youtube.com")]
    public void Constructor_KeepsOnlyTheBareDomain(string typed, string domain)
    {
        new Website(typed).Url.Should().Be(domain);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://")]
    [InlineData("/path")]
    [InlineData("you tube.com")]
    public void Constructor_NothingThatIsADomain_Throws(string? typed)
    {
        var act = () => new Website(typed!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryCreate_ValidInput_GivesTheWebsite()
    {
        Website.TryCreate("https://www.youtube.com/", out var site).Should().BeTrue();

        site.Should().Be(new Website("youtube.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://")]
    [InlineData("you tube.com")]
    public void TryCreate_NothingThatIsADomain_RefusesWithoutThrowing(string? typed)
    {
        Website.TryCreate(typed, out var site).Should().BeFalse();

        site.Should().BeNull();
    }

    [Fact]
    public void TwoWaysOfTypingTheSameSite_AreEqual()
    {
        new Website("https://www.youtube.com/").Should().Be(new Website("youtube.com"));
    }

    [Theory]
    [InlineData("youtube.com", "youtube.com")]
    [InlineData("youtube.com", "m.youtube.com")]
    [InlineData("youtube.com", "a.b.youtube.com")]
    public void Covers_ItselfAndItsSubdomains(string listed, string visited)
    {
        new Website(listed).Covers(new Website(visited)).Should().BeTrue();
    }

    [Theory]
    [InlineData("youtube.com", "notyoutube.com")]
    [InlineData("youtube.com", "youtube.com.evil.com")]
    [InlineData("mail.google.com", "google.com")]
    [InlineData("mail.google.com", "docs.google.com")]
    public void Covers_NothingElse(string listed, string visited)
    {
        new Website(listed).Covers(new Website(visited)).Should().BeFalse();
    }

    [Fact]
    public void ToString_ReturnsUrl()
    {
        new Website("facebook.com").ToString().Should().Be("facebook.com");
    }
}
