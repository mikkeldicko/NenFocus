using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NenFocus.Converters;

/// <summary>
/// A time of day as "HH:mm" text and back — the one place either is done (Gap 89 Slice B), used by
/// the TimeOfDayBox control's text and, later, by a 12/24-hour setting (Gap 83). "24:00" is the end
/// of the day (24 hours). Text that isn't a time is no time (null), so a ViewModel never sees
/// half-typed text.
/// </summary>
public sealed class TimeOfDayToStringConverter : IValueConverter
{
    public static readonly TimeOfDayToStringConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is TimeSpan time ? Format(time) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string text && TryParse(text, out var time) ? time : null;

    /// <summary>For the TimeOfDayBox control, which also needs the text for its dropdown items.</summary>
    public static string Format(TimeSpan time) => $"{(int)time.TotalHours:D2}:{time.Minutes:D2}";

    /// <summary>
    /// TimeSpan.TryParse alone reads "93" as 93 days, which would place a block 93 days out — so
    /// anything with a day part is refused, and "24:00" (which it refuses) is let in by name.
    /// </summary>
    public static bool TryParse(string text, out TimeSpan time)
    {
        if (text == "24:00") { time = TimeSpan.FromHours(24); return true; }
        if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out time) && time.Days == 0 && time >= TimeSpan.Zero)
            return true;
        time = default;
        return false;
    }
}
