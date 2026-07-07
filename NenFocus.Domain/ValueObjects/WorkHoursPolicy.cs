namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// The work-hours constraint for a Nen Contract: a default daily window plus optional
/// per-day overrides. Bundled as one concept because they are always configured and
/// consumed together — e.g. "9-5 weekdays, but 11-8 on Wednesdays, no constraint Saturday".
/// </summary>
public sealed record WorkHoursPolicy
{
    public WorkHours? Default { get; }
    public IReadOnlyDictionary<DateOnly, WorkHours?> PerDayExceptions { get; }

    public WorkHoursPolicy(WorkHours? @default, IReadOnlyDictionary<DateOnly, WorkHours?>? perDayExceptions = null)
    {
        Default = @default;
        PerDayExceptions = perDayExceptions ?? new Dictionary<DateOnly, WorkHours?>();
    }

    /// <summary>
    /// Resolves the effective work hours for a given day: a per-day exception wins
    /// (including an explicit null meaning "no constraint that day"), otherwise the
    /// default applies, otherwise there is no constraint at all.
    /// </summary>
    public WorkHours? For(DateOnly date)
        => PerDayExceptions.TryGetValue(date, out var exception) ? exception : Default;
}
