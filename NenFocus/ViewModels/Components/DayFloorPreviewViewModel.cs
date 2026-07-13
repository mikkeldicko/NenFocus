using System;

namespace NenFocus.ViewModels.Components;

/// <summary>
/// Live per-day floor status shown on the Planner while drafting a Nen Contract.
/// Rebuilt fresh (via ComputePlannedFocusHoursQuery) whenever the draft blocks or the
/// visible week changes — not observable, the whole collection is replaced instead.
/// </summary>
public sealed record DayFloorPreviewViewModel(
    DateOnly Date,
    double ScheduledHours,
    double? RequiredFloorHours,
    bool IsRestDay)
{
    public bool IsMet => IsRestDay || RequiredFloorHours is null || ScheduledHours >= RequiredFloorHours.Value;

    public string Label => IsRestDay
        ? "Rest"
        : RequiredFloorHours.HasValue
            ? $"{ScheduledHours:0.#}h / {RequiredFloorHours.Value:0.#}h"
            : $"{ScheduledHours:0.#}h";
}
