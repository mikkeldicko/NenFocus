using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests;

/// <summary>
/// Builds a contract's days the way the Seal/Edit handlers do when no start time is given —
/// starting at the start date's Work Hours start (or midnight) — so tests can describe a
/// contract by its dates and floor rule. Gap 65.
/// </summary>
internal static class TestCalendars
{
    private static readonly IReadOnlyDictionary<DateOnly, double> NoPlan = new Dictionary<DateOnly, double>();

    public static ContractCalendar For(
        DateOnly startDate,
        DateOnly endDate,
        DailyFloorRule floorRule,
        IReadOnlyDictionary<DateOnly, double>? plannedFocusHoursByDay = null,
        WorkHours? workHours = null,
        IReadOnlyCollection<DateOnly>? restDays = null,
        IReadOnlyCollection<DateOnly>? noWorkHoursDays = null)
    {
        var noWorkHours = noWorkHoursDays ?? [];
        return ContractCalendar.Generate(
            ContractCalendar.SuggestedStart(startDate, workHours, noWorkHours, DateTime.MinValue),
            endDate,
            endTime: null,
            workHours,
            noWorkHours,
            floorRule,
            restDays ?? [],
            plannedFocusHoursByDay ?? NoPlan);
    }
}
