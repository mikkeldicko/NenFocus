using System;

namespace NenFocus.Converters;

/// <summary>"09:00 – 17:00" for a set Work Hours window, "No constraint" when none is fixed —
/// was independently hand-rolled in ContractPresetCardViewModel, ContractRulesStepViewModel,
/// and ContractWizardViewModel; consolidated here so the three copies can't drift apart. No
/// IValueConverter here (unlike this codebase's other formatters) — Start and End are two
/// separate bound values with no MultiBinding precedent in this codebase, so this is a plain
/// static entry point a ViewModel calls directly, the same idiom
/// MinutesToStringConverter.FormatMinutes already established for exactly this situation.</summary>
public static class WorkHoursRangeToLabelConverter
{
    public static string FormatRange(TimeOnly? start, TimeOnly? end)
        => start is { } s && end is { } e ? $"{s:HH:mm} – {e:HH:mm}" : "No constraint";

    /// <summary>Bare "9:00 AM – 5:00 PM" range, no "No constraint" fallback — for a caller
    /// that already knows the window exists (e.g. a live placement-validation message quoting
    /// the Work Hours window it just rejected against).</summary>
    public static string FormatKnownWindow12Hour(TimeOnly start, TimeOnly end) => $"{start:h:mm tt} – {end:h:mm tt}";
}
