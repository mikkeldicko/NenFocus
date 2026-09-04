namespace NenFocus.Domain.Enums;

/// <summary>
/// Which days a redemption Focus block may originate a new claim against an outstanding
/// deficit — independent of StrictnessRule, which governs whether an already-placed block
/// can move afterward (see NenContract.AllowsBlockTimeChange). AnywhereInContract is the
/// default (also the enum's default value) — matches int 0, so callers that don't specify
/// it (existing tests, Simple contracts) get the more permissive setting.
/// </summary>
public enum RedemptionPlacementPolicy
{
    AnywhereInContract,
    CurrentDayOnly
}
