namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// A holder's own words on how something went, plus whether the screen that asks for them was
/// dismissed (Gap 114, D3). One value object shared by Session (the run), a missed FocusBlock, and
/// NenContract — replacing each holder's duplicated <c>string? + Moment?</c> pair. The trim/blank
/// rule lives here: a blank or whitespace-only note is stored as none. Each holder keeps its own
/// "only once ended" guard — this value object only carries the two facts and their trim rule.
/// </summary>
public sealed record Reflection
{
    public string? Text { get; init; }
    public Moment? AcknowledgedAt { get; init; }

    public Reflection(string? text, Moment? acknowledgedAt = null)
    {
        Text = Normalize(text);
        AcknowledgedAt = acknowledgedAt;
    }

    /// <summary>The note, changed or cleared — blank becomes none.</summary>
    public Reflection WithText(string? text) => this with { Text = Normalize(text) };

    /// <summary>The note, stamped as seen.</summary>
    public Reflection Acknowledged(Moment at) => this with { AcknowledgedAt = at };

    private static string? Normalize(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
