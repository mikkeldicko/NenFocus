using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

public sealed class PlannedFocusHoursCalculator : IPlannedFocusHoursCalculator
{
    public IReadOnlyDictionary<DateOnly, double> Calculate(IEnumerable<TimeSlot> focusTime, UserTimeZone timeZone)
    {
        var hoursByDay = new Dictionary<DateOnly, double>();

        foreach (var slot in focusTime)
        {
            foreach (var (date, part) in timeZone.SplitByDay(slot))
                hoursByDay[date] = hoursByDay.GetValueOrDefault(date) + part.Length.TotalHours;
        }

        return hoursByDay;
    }
}
