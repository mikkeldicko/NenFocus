namespace NenFocus.Domain.Services;

public sealed class PlannedFocusHoursCalculator : IPlannedFocusHoursCalculator
{
    public IReadOnlyDictionary<DateOnly, double> Calculate(IEnumerable<(DateTime Start, DateTime End)> focusBlockRanges)
    {
        var hoursByDay = new Dictionary<DateOnly, double>();

        foreach (var (start, end) in focusBlockRanges)
        {
            foreach (var (date, segmentStart, segmentEnd) in DayRangeSplitter.Split(start, end))
            {
                var hours = (segmentEnd - segmentStart).TotalHours;
                hoursByDay[date] = hoursByDay.GetValueOrDefault(date) + hours;
            }
        }

        return hoursByDay;
    }
}
