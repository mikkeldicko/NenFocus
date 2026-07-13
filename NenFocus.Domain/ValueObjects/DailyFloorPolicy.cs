namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// The daily-floor constraint for a Nen Contract: a default floor applied to every day
/// plus optional per-day overrides. Mirrors WorkHoursPolicy's shape. A resolved floor of
/// null means that day is a rest day — no obligation, skipped by Seal/Extend validation
/// and trivially met by IsDayMet. Advanced contracts with one fixed floor use only
/// Default; Simple contracts (whose floor varies per day, equal to that day's planned
/// hours) populate PerDayExceptions explicitly for every day and leave Default null.
/// </summary>
public sealed record DailyFloorPolicy
{
    public DailyFloor? Default { get; }
    public IReadOnlyDictionary<DateOnly, DailyFloor?> PerDayExceptions { get; }

    public DailyFloorPolicy(DailyFloor? @default, IReadOnlyDictionary<DateOnly, DailyFloor?>? perDayExceptions = null)
    {
        Default = @default;
        PerDayExceptions = perDayExceptions ?? new Dictionary<DateOnly, DailyFloor?>();
    }

    /// <summary>No floor on any day — every day is a rest day.</summary>
    public static DailyFloorPolicy None => new(null, null);

    /// <summary>One fixed floor for every day, except the given rest days, which carry no floor at all.</summary>
    public static DailyFloorPolicy Fixed(DailyFloor @default, IReadOnlyCollection<DateOnly>? restDays = null)
        => new(@default, RestDayExceptions(restDays ?? Array.Empty<DateOnly>()));

    /// <summary>
    /// No shared default — each day's floor is given explicitly. A day absent from the
    /// map has no floor at all (a rest day).
    /// </summary>
    public static DailyFloorPolicy PerDay(IReadOnlyDictionary<DateOnly, double> hoursByDay)
        => new(null, PerDayExceptionsFrom(hoursByDay));

    /// <summary>
    /// Builds a PerDayExceptions map with an explicit null (rest day) for each given date.
    /// Exposed separately from Fixed(...) so callers merging into an existing policy's
    /// exceptions (e.g. Extend, which adds new days rather than replacing the policy) can
    /// reuse the same conversion instead of re-deriving it.
    /// </summary>
    public static IReadOnlyDictionary<DateOnly, DailyFloor?> RestDayExceptions(IReadOnlyCollection<DateOnly> restDays)
        => restDays.ToDictionary(date => date, _ => (DailyFloor?)null);

    /// <summary>
    /// Builds a PerDayExceptions map with an explicit floor equal to each day's own hours.
    /// Exposed separately from PerDay(...) for the same merging reason as RestDayExceptions.
    /// </summary>
    public static IReadOnlyDictionary<DateOnly, DailyFloor?> PerDayExceptionsFrom(IReadOnlyDictionary<DateOnly, double> hoursByDay)
        => hoursByDay.ToDictionary(kv => kv.Key, kv => (DailyFloor?)new DailyFloor(kv.Value));

    /// <summary>
    /// Resolves the effective floor for a given day: a per-day exception wins
    /// (including an explicit null meaning "rest day"), otherwise the default
    /// applies, otherwise there is no floor at all (also a rest day).
    /// </summary>
    public DailyFloor? For(DateOnly date)
        => PerDayExceptions.TryGetValue(date, out var exception) ? exception : Default;
}
