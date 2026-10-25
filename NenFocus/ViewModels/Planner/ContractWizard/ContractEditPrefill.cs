using System;
using System.Collections.Generic;
using System.Linq;
using NenFocus.Application.DTOs;

namespace NenFocus.ViewModels.Planner.ContractWizard;

/// <summary>
/// TEMPORARY (Gap 65 → deleted by Open Gap W). Today's wizard still collects one Work Hours
/// window, one fixed floor and a rest-day list, while a contract now stores its days (the
/// only truth). This fills those wizard fields from the days so Edit re-opens as it was
/// sealed. Display prefill only — no rule lives here. Known blind spot: a one-day contract
/// that started after its Work Hours opened reports that day's shortened window.
/// </summary>
public sealed record ContractEditPrefill(
    bool IsAdvanced,
    double? FixedDailyFloorHours,
    IReadOnlyList<DateOnly> RestDays,
    TimeOnly? WorkHoursStart,
    TimeOnly? WorkHoursEnd)
{
    public static ContractEditPrefill From(NenContractEditDto contract)
    {
        var isAdvanced = contract.FixedDailyFloorHours.HasValue;

        var restDays = isAdvanced
            ? contract.Days.Where(d => d.FloorHours is null).Select(d => d.Date).ToList()
            : (IReadOnlyList<DateOnly>)[];

        // A Work Hours window always ends before midnight; a window running to midnight is
        // a no-Work-Hours day. Day 1 may have been shortened by a late start, so middle days
        // decide first, then the last day, and day 1 only when it's the only candidate.
        var workHoursDays = contract.Days
            .Where(d => d.WindowEnd < d.Date.AddDays(1).ToDateTime(TimeOnly.MinValue))
            .ToList();
        var representative = workHoursDays
            .Where(d => d.Date != contract.Days[0].Date && d.Date != contract.Days[^1].Date)
            .GroupBy(d => (TimeOnly.FromDateTime(d.WindowStart), TimeOnly.FromDateTime(d.WindowEnd)))
            .OrderByDescending(g => g.Count())
            .Select(g => g.First())
            .FirstOrDefault() ?? workHoursDays.LastOrDefault();

        return new ContractEditPrefill(
            isAdvanced,
            contract.FixedDailyFloorHours,
            restDays,
            representative is null ? null : TimeOnly.FromDateTime(representative.WindowStart),
            representative is null ? null : TimeOnly.FromDateTime(representative.WindowEnd));
    }
}
