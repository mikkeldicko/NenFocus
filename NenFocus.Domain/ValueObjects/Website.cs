using System.Diagnostics.CodeAnalysis;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// A site, as a bare domain (<c>youtube.com</c>) — the one definition the blocklist, the slice, the hosts
/// file and the extension's check share (Gap 112, I2). Whatever was typed or visited
/// (<c>https://www.YouTube.com/watch?v=1</c>) is cut down to its domain on construction, so two ways of
/// writing one site are equal. Allowed or blocked is which list it's in, not a fact about the site.
/// </summary>
public record Website
{
    public string Url { get; }

    public Website(string url)
    {
        Url = DomainOf(url)
            ?? throw new ArgumentException($"'{url}' is not a website.", nameof(url));
    }

    /// <summary>The non-throwing ask, for input from a person or a browser.</summary>
    public static bool TryCreate(string? input, [NotNullWhen(true)] out Website? site)
    {
        site = DomainOf(input) is { } domain ? new Website(domain) : null;
        return site is not null;
    }

    /// <summary>A site covers itself and every subdomain: youtube.com covers m.youtube.com, not notyoutube.com.</summary>
    public bool Covers(Website other) =>
        other.Url == Url || other.Url.EndsWith("." + Url, StringComparison.Ordinal);

    public override string ToString() => Url;

    private static string? DomainOf(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var s = input.Trim().ToLowerInvariant();
        var scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];

        var end = s.IndexOfAny(['/', '?', '#', ':']);
        if (end >= 0) s = s[..end];

        if (s.StartsWith("www.", StringComparison.Ordinal)) s = s[4..];
        s = s.TrimEnd('.');

        return s.Length == 0 || s.Any(char.IsWhiteSpace) ? null : s;
    }
}
