using System;
using NenFocus.Converters;
using NenFocus.Domain.Enums;

namespace NenFocus.ViewModels.Components;

/// <summary>
/// Live per-day floor status shown on the Planner, either while drafting a Nen
/// Contract (rebuilt via ComputePlannedFocusHoursQuery whenever the draft blocks or
/// the visible week changes) or for an already-sealed/active contract (rebuilt via
/// GetNenContractsForDateRangeQuery's DayStatuses). Not observable — the whole collection is
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
/// True only for a Period Time Budget contract held to a whole-period target
/// instead of a per-day floor (see NenContractRangeViewDto.HasPeriodTarget). RequiredFloorHours
/// still carries a comparison number in this mode — that day's currently-planned hours,
/// not a real obligation — which is why Past drops the "/planned" half entirely: a day's
/// shortfall there isn't a boolean pass/fail the way a real floor is, it's "this many
/// hours still need making up," which the single completed-hours number plus red/green
/// already says.
/// </param>
/// <param name="BonusHours">
/// Completed hours from the contract's Bonus blocks on this day — genuinely separate
/// from CurrentHours/RequiredFloorHours above, never folded into the floor badge itself.
/// Zero while drafting (a brand-new contract has no blocks yet to be Bonus to).
/// </param>
/// <param name="ContractStatus">
/// The owning contract's real Status (Scheduled or Active) — null while drafting, same
/// nullability convention as ContractStrictnessRule. Every day belonging to a Scheduled
/// (Upcoming) contract renders as a uniform passive grey regardless of anything else about
/// it (met/unmet/rest) — see DayFloorStatusToBrushConverter — since none of that is
/// actionable until the contract actually starts. An Active contract's rest day, by
/// contrast, reads as green ("nothing left to do today"), now that grey means Upcoming
/// exclusively instead of "rest" generically.
/// </param>
/// <param name="IsMet">
/// Passed in, never re-derived from CurrentHours/RequiredFloorHours here — for a sealed/
/// active contract this is NenContractDayStatusDto.IsMet exactly as GetNenContractsForDateRangeQueryHandler
/// already decided; the ViewModel trusting a second, independently-computed copy of the same
/// comparison was itself a bug risk (the two could silently disagree). Drafting has no
/// Application-layer answer to defer to, so RefreshDayFloorPreviewsAsync computes it locally
/// and passes it in like every other consumer. Defaults to true only so a caller that
/// genuinely has no floor to compare against (IsRestDay) doesn't need to pass anything.
/// </param>
public sealed record DayFloorPreviewViewModel(
    DateOnly Date,
    double CurrentHours,
    double? RequiredFloorHours,
    bool IsRestDay,
    DayTiming Timing = DayTiming.Future,
    StrictnessRule? ContractStrictnessRule = null,
    bool IsContractFloorMode = false,
    double BonusHours = 0,
    NenContractStatus? ContractStatus = null,
    bool IsMet = true)
{
    public bool HasBonus => BonusHours > 0;
    public string BonusLabel => $"+{HoursToStringConverter.FormatHours(BonusHours)} bonus";

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

    // IsRestDay/RequiredFloorHours already carry whatever the Application layer resolved
    // (a real committed floor stays pinned; an originally-rest day that later got a
    // floor-counting block gets an ad-hoc floor and IsRestDay=false) — no special-casing
    // needed here, this already renders correctly either way.
    public string Label => IsRestDay
        ? "Rest"
        : IsContractFloorMode && Timing == DayTiming.Past
            ? HoursToStringConverter.FormatHours(CurrentHours)
            : ShowSimplifiedFuture
                ? HoursToStringConverter.FormatHours(CurrentHours)
                : RequiredFloorHours.HasValue
                    ? $"{HoursToStringConverter.FormatHours(CurrentHours)} / {HoursToStringConverter.FormatHours(RequiredFloorHours.Value)}"
                    : HoursToStringConverter.FormatHours(CurrentHours);
}
