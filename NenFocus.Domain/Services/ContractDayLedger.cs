using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

/// <summary>
/// The arithmetic behind <see cref="NenContract.DayStatuses"/> (Gap 95, decisions 5 and 6), kept
/// apart so the contract only says whose blocks and which days. Every hour is split across the
/// local days it falls on.
/// </summary>
internal static class ContractDayLedger
{
    internal static IReadOnlyList<ContractDayStatus> Build(
        IReadOnlyList<DateOnly> days,
        IReadOnlyList<FocusBlock> focusBlocks,
        IEnumerable<Session> sessions,
        Moment asOf,
        UserTimeZone timeZone)
    {
        var target = new Dictionary<DateOnly, double>();
        var completed = new Dictionary<DateOnly, double>();
        var bonus = new Dictionary<DateOnly, double>();

        // What a day owes: its Committed blocks as planned — missed, ended early or cancelled, a
        // Committed block still counts in full.
        foreach (var block in focusBlocks.Where(b => b.IsCommitted))
            AddHours(target, block.Scheduled, timeZone);

        // Plus what its redemptions cover: once per missed block per day, never more than that
        // block's own deficit — two attempts at one miss on one day owe it once.
        var blocksById = focusBlocks.ToDictionary(b => b.Id);
        var coverage = focusBlocks
            .Where(b => b.IsRedemption)
            .SelectMany(b => b.Receipt.Entries.Select(e => (Date: timeZone.DateOf(b.Scheduled.Start), e.MissedBlockId, Covering: b)))
            .GroupBy(x => (x.Date, x.MissedBlockId));
        foreach (var claims in coverage)
        {
            var claimed = claims.Sum(x => x.Covering.Receipt.MinutesFor(x.MissedBlockId));
            var owed = blocksById.TryGetValue(claims.Key.MissedBlockId, out var missed)
                ? Math.Min(claimed, missed.OwnDeficit(asOf).TotalMinutes)
                : claimed;
            target[claims.Key.Date] = target.GetValueOrDefault(claims.Key.Date) + owed / 60;
        }

        // What was done toward it: the credit of every block that counts toward the floor…
        foreach (var block in focusBlocks.Where(b => b.CountsTowardFloor))
            if (block.CreditedSlot(asOf) is { } credited)
                AddHours(completed, credited, timeZone);

        // …and, until Gap 67 makes them Bonus, spontaneous sessions on one of those blocks'
        // presets that started on one of the contract's days.
        var presets = focusBlocks.Where(b => b.CountsTowardFloor).Select(b => b.BlockPresetId).ToHashSet();
        var firstDay = days[0];
        var lastDay = days[^1];
        foreach (var session in sessions)
        {
            if (session.Origin != SessionOrigin.Spontaneous || !presets.Contains(session.BlockPresetId)) continue;
            if (session.StartedAt is not { } startedAt) continue;
            var startDay = timeZone.DateOf(startedAt);
            if (startDay < firstDay || startDay > lastDay) continue;
            if (session.GetElapsedTime(asOf) is { } elapsed && elapsed > TimeSpan.Zero)
                AddHours(completed, new TimeSlot(startedAt, startedAt + elapsed), timeZone);
        }

        // Bonus blocks earn hours of their own, never toward the target.
        foreach (var block in focusBlocks.Where(b => b.IsBonus))
            if (block.CreditedSlot(asOf) is { } credited)
                AddHours(bonus, credited, timeZone);

        return days.Select(date =>
        {
            var owed = target.GetValueOrDefault(date);
            var done = completed.GetValueOrDefault(date);
            var isRest = owed <= 0;
            return new ContractDayStatus(date, owed, done, bonus.GetValueOrDefault(date), isRest, IsMet: isRest || done >= owed);
        }).ToList();
    }

    private static void AddHours(Dictionary<DateOnly, double> hoursByDay, TimeSlot slot, UserTimeZone timeZone)
    {
        foreach (var (date, part) in timeZone.SplitByDay(slot))
            hoursByDay[date] = hoursByDay.GetValueOrDefault(date) + part.Length.TotalHours;
    }
}
