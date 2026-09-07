using System;

namespace NenFocus.Converters;

/// <summary>"Jan 1, 2026 – Jan 31, 2026" for a DateOnly range. Plain static entry point (no
/// MultiBinding precedent in this codebase for a two-value XAML converter) — same idiom as
/// MinutesToStringConverter.FormatMinutes/DailyFloorToLabelConverter.FormatFloor.</summary>
public static class DateRangeToLabelConverter
{
    public static string FormatRange(DateOnly start, DateOnly end) => $"{start:MMM d, yyyy} – {end:MMM d, yyyy}";

    /// <summary>"Jan 1 – Jan 31" — the shorter form used for validation messages that don't need the year.</summary>
    public static string FormatRangeShort(DateOnly start, DateOnly end) => $"{start:MMM d} – {end:MMM d}";

    /// <summary>"Jan 1" — a single date, same short form.</summary>
    public static string FormatDateShort(DateOnly date) => $"{date:MMM d}";
}
