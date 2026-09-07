using System;

namespace NenFocus.Converters;

/// <summary>"09:00 – 17:00" for a pair of already-local DateTimes — plain static entry point,
/// same idiom as this file's siblings, for a caller (e.g. a live block-overlap validation
/// message) that needs the formatted range woven into a larger sentence rather than bound
/// directly in XAML.</summary>
public static class DateTimeRangeToLabelConverter
{
    public static string FormatRange24Hour(DateTime start, DateTime end) => $"{start:HH:mm} – {end:HH:mm}";
}
