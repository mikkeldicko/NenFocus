using System;
using NenFocus.Domain.Enums;

namespace NenFocus.ViewModels.Components;

/// <summary>
/// Live per-day floor status shown on the Planner, either while drafting a Nen
/// Contract (rebuilt via ComputePlannedFocusHoursQuery whenever the draft blocks or
/// the visible week changes) or for an already-sealed/active contract (rebuilt via
/// GetActiveNenContractQuery's DayStatuses). Not observable — the whole collection is
/// replaced instead. Timing defaults to Future while drafting (draft blocks can only
/// ever be placed in the future), so drafting badges always render as "planned"; only
/// a sealed contract's Today/Past days ever render as "in progress" or "completed".
/// </summary>
public sealed record DayFloorPreviewViewModel(
    DateOnly Date,
    double CurrentHours,
    double? RequiredFloorHours,
    bool IsRestDay,
    DayTiming Timing = DayTiming.Future)
{
    public bool IsMet => IsRestDay || RequiredFloorHours is null || CurrentHours >= RequiredFloorHours.Value;

    public string Label => IsRestDay
        ? "Rest"
        : RequiredFloorHours.HasValue
            ? $"{CurrentHours:0.#}h / {RequiredFloorHours.Value:0.#}h"
            : $"{CurrentHours:0.#}h";
}
