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
/// <param name="ContractStrictnessRule">
/// Null while drafting, set to the contract's actual StrictnessRule for any sealed/active
/// contract — this is really "is this a sealed contract's badge, not a draft preview,"
/// not a signal tied to one particular strictness value. Drafting deliberately keeps the
/// two-number met/unmet feedback (that's the whole point of the live preview — it's the
/// gate itself, not a shortcut past it); a sealed contract's future day never can, for any
/// strictness rule, since Seal/Edit's own invariant plus UpdatePlannedBlockValidator's
/// floor-protection guards make every future day provably safe by the time it's shown.
/// </param>
/// <param name="IsContractFloorMode">
/// True only for a Period Time Budget contract using a whole-period ContractFloor
/// instead of a per-day floor (see ActiveNenContractDto.ContractFloorHours). RequiredFloorHours
/// still carries a comparison number in this mode — that day's currently-planned hours,
/// not a real obligation — which is why Past drops the "/planned" half entirely: a day's
/// shortfall there isn't a boolean pass/fail the way a real floor is, it's "this many
/// hours still need making up," which the single completed-hours number plus red/green
/// already says.
/// </param>
public sealed record DayFloorPreviewViewModel(
    DateOnly Date,
    double CurrentHours,
    double? RequiredFloorHours,
    bool IsRestDay,
    DayTiming Timing = DayTiming.Future,
    StrictnessRule? ContractStrictnessRule = null,
    bool IsContractFloorMode = false)
{
    public bool IsMet => IsRestDay || RequiredFloorHours is null || CurrentHours >= RequiredFloorHours.Value;

    // Unified across every strictness rule: a sealed contract's future day never needs a
    // met/unmet comparison shown, since it's provably always at-or-above its floor by the
    // time it's future (Seal/Edit's own check, plus the post-seal floor-protection guards).
    // Only a live drafting preview (ContractStrictnessRule null) still shows the two-number
    // comparison, since that comparison is the actionable feedback drafting exists to give.
    // "At-or-above" still means the exceeds case is worth showing, though: a day scheduled
    // with more than a real fixed floor requires (e.g. 2h planned against a 1h floor) has
    // genuine surplus worth surfacing — only an exact match collapses to one number, and it
    // collapses back the moment an edit brings it down to exactly the floor again.
    public bool ShowSimplifiedFuture =>
        !IsRestDay && Timing == DayTiming.Future && ContractStrictnessRule.HasValue
        && (!RequiredFloorHours.HasValue || CurrentHours <= RequiredFloorHours.Value);

    // TODO: rest-day/floor-counting-vs-Bonus redesign in progress (see TECHNICAL_SPEC_INFRA.md)
    // — CurrentHours below no longer gets zeroed for a rest day (that part's correct and
    // final), but this label still needs the ad-hoc-floor + genuinely-separate-Bonus split
    // before it's done. Left as plain "Rest" for now rather than the wrong "Bonus:" wording.
    public string Label => IsRestDay
        ? "Rest"
        : IsContractFloorMode && Timing == DayTiming.Past
            ? $"{CurrentHours:0.#}h"
            : ShowSimplifiedFuture
                ? $"{CurrentHours:0.#}h"
                : RequiredFloorHours.HasValue
                    ? $"{CurrentHours:0.#}h / {RequiredFloorHours.Value:0.#}h"
                    : $"{CurrentHours:0.#}h";
}
