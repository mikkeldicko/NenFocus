namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// A Nen Contract's days — an unbroken run, exactly one ContractDay per date. The single
/// source of truth for when the contract begins and ends (StartsAt/EndsAt are read off the
/// first and last day, never stored) and for each day's window and floor. Private to
/// NenContract: callers ask the contract named questions instead of reading days. Gap 65.
/// </summary>
public sealed class ContractCalendar
{
    public IReadOnlyList<ContractDay> Days { get; }

    public ContractCalendar(IEnumerable<ContractDay> days)
    {
        var ordered = days.OrderBy(d => d.Date).ToList();

        if (ordered.Count == 0)
            throw new ArgumentException("A contract must have at least one day.");

        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Date != ordered[i - 1].Date.AddDays(1))
                throw new ArgumentException("A contract's days must be one per date with no gaps.");
        }

        Days = ordered;
    }

    public DateOnly FirstDate => Days[0].Date;
    public DateOnly LastDate => Days[^1].Date;
    public DateTime StartsAt => Days[0].WindowStart;
    public DateTime EndsAt => Days[^1].WindowEnd;

    public bool HasAnyDailyFloor => Days.Any(d => d.Floor is not null);

    public ContractDay? DayOn(DateOnly date)
    {
        var index = date.DayNumber - FirstDate.DayNumber;
        return index >= 0 && index < Days.Count ? Days[index] : null;
    }

    /// <summary>
    /// The same calendar with every day after <paramref name="lastDate"/> removed — the
    /// last kept day stays whole. Never shrinks below the first day.
    /// </summary>
    public ContractCalendar EndingOn(DateOnly lastDate)
    {
        if (lastDate >= LastDate) return this;
        var keep = lastDate < FirstDate ? FirstDate : lastDate;
        return new ContractCalendar(Days.Where(d => d.Date <= keep));
    }

    /// <summary>
    /// Builds the days from what a contract is sealed with — the Work Hours, rest days and
    /// planned hours are used here once and never stored. Each day's window is the Work
    /// Hours (or the whole day on a no-Work-Hours day, or when there are no Work Hours);
    /// day 1's window then starts at <paramref name="localStart"/>. The last day ends at
    /// <paramref name="endTime"/> only when there are no Work Hours (null = end of day) —
    /// with Work Hours the end is always the last day's own window end. Each day's floor
    /// follows <paramref name="floorRule"/>; a floor that doesn't fit its day's window is refused, never shrunk.
    /// <paramref name="restDays"/> only apply to a fixed floor.
    /// </summary>
    public static ContractCalendar Generate(
        DateTime localStart,
        DateOnly endDate,
        TimeOnly? endTime,
        WorkHours? workHours,
        IReadOnlyCollection<DateOnly> noWorkHoursDays,
        DailyFloorRule floorRule,
        IReadOnlyCollection<DateOnly> restDays,
        IReadOnlyDictionary<DateOnly, double> plannedFocusHoursByDay)
    {
        var startDate = DateOnly.FromDateTime(localStart);

        if (endDate < startDate)
            throw new ArgumentException("End date must be on or after the start date.");

        if (workHours is not null && endTime is not null)
            throw new ArgumentException("With Work Hours, the contract ends at the last day's Work Hours end — no separate end time.");

        var days = new List<ContractDay>();
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var (windowStart, windowEnd) = DefaultWindow(date, workHours, noWorkHoursDays);
            var dayStart = date.ToDateTime(TimeOnly.MinValue);

            if (date == startDate)
            {
                if (localStart >= windowEnd)
                    throw new ArgumentException($"The contract starts at {localStart:HH:mm}, after that day's hours end.");
                windowStart = localStart;
            }

            if (date == endDate && endTime is { } end)
                windowEnd = dayStart + end.ToTimeSpan();

            var floor = floorRule switch
            {
                DailyFloorRule.Fixed when restDays.Contains(date) => null,
                DailyFloorRule.Fixed f => f.Floor,
                DailyFloorRule.FollowsPlan => plannedFocusHoursByDay.TryGetValue(date, out var hours) ? new DailyFloor(hours) : null,
                _ => null
            };

            // Never shrunk to fit — a floor that no longer fits (a late same-day start, or
            // drafting that ran long) is refused, so a day's floor can't move under the user.
            if (floor is not null && floor.Hours > (windowEnd - windowStart).TotalHours)
                throw new ArgumentException(
                    $"Not enough time left on {date:ddd d MMM} for its {floor.Hours:0.##}h floor — start on a later day or lower the floor.");

            days.Add(new ContractDay(date, windowStart, windowEnd, floor));
        }

        return new ContractCalendar(days);
    }

    /// <summary>
    /// The start to suggest for a contract beginning on <paramref name="date"/> — that day's
    /// Work Hours start (or midnight), unless that moment has already passed today, in which
    /// case the current minute. A date already in the past keeps its default, so Seal's own
    /// "start can't be in the past" rule reports it rather than it being silently moved.
    /// </summary>
    public static DateTime SuggestedStart(
        DateOnly date, WorkHours? workHours, IReadOnlyCollection<DateOnly> noWorkHoursDays, DateTime localNow)
    {
        var defaultStart = DefaultWindow(date, workHours, noWorkHoursDays).Start;
        var currentMinute = new DateTime(localNow.Ticks - localNow.Ticks % TimeSpan.TicksPerMinute, localNow.Kind);
        return date == DateOnly.FromDateTime(localNow) && currentMinute > defaultStart ? currentMinute : defaultStart;
    }

    private static (DateTime Start, DateTime End) DefaultWindow(DateOnly date, WorkHours? workHours, IReadOnlyCollection<DateOnly> noWorkHoursDays)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        return workHours is not null && !noWorkHoursDays.Contains(date)
            ? (dayStart + workHours.Start.ToTimeSpan(), dayStart + workHours.End.ToTimeSpan())
            : (dayStart, dayStart.AddDays(1));
    }
}
