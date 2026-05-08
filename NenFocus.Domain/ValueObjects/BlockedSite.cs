namespace NenFocus.Domain.ValueObjects;

public record BlockedSite
{
    public string Url { get; }

    public BlockedSite(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be empty.");

        Url = url.Trim().ToLowerInvariant();
    }

    public override string ToString() => Url;
}