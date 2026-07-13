namespace NenFocus.Domain.Services;

public sealed class PlannedFocusHoursCalculator : IPlannedFocusHoursCalculator
{
    public IReadOnlyDictionary<DateOnly, double> Calculate(IEnumerable<(DateTime Start, DateTime End)> focusBlockRanges)
    {
        var hoursByDay = new Dictionary<DateOnly, double>();

        foreach (var (start, end) in focusBlockRanges)
        {
            if (end <= start)
                continue;

            var cursor = start;
            while (cursor < end)
            {
                var nextDayStart = cursor.Date.AddDays(1);
                var segmentEnd = end < nextDayStart ? end : nextDayStart;

                var date = DateOnly.FromDateTime(cursor.Date);
                var hours = (segmentEnd - cursor).TotalHours;
                hoursByDay[date] = hoursByDay.GetValueOrDefault(date) + hours;

                cursor = segmentEnd;
            }
        }

        return hoursByDay;
    }
}
