namespace NenFocus.Application.Queries.CheckRedemptionEntry;

public enum RedemptionEntryCheckResult
{
    Valid,

    /// <summary>The referenced block doesn't exist, isn't a FocusBlock, or isn't linked to a contract.</summary>
    MissedBlockNotFound,

    /// <summary>Rule 4 — the redemption block must share the missed block's preset.</summary>
    PresetMismatch,

    /// <summary>Rule 10 — a Locked contract has no redemption recovery mechanism at all.</summary>
    StrictnessDoesNotAllowRedemption,

    /// <summary>Rule 8 — DailyFlexibility requires same-day coverage.</summary>
    WrongDay,

    /// <summary>The referenced block's remaining deficit can't absorb this many minutes.</summary>
    ExceedsRemainingDeficit
}
