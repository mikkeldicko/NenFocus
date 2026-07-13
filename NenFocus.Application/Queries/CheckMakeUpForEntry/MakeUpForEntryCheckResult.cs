namespace NenFocus.Application.Queries.CheckMakeUpForEntry;

public enum MakeUpForEntryCheckResult
{
    Valid,

    /// <summary>The referenced block doesn't exist, isn't a FocusBlock, or isn't linked to a contract.</summary>
    MissedBlockNotFound,

    /// <summary>Rule 4 — the make-up block must share the missed block's preset.</summary>
    PresetMismatch,

    /// <summary>Rule 10 — a Locked contract has no make-up recovery mechanism at all.</summary>
    StrictnessDoesNotAllowMakeUp,

    /// <summary>Rules 8/10 — DailyFlexibility and DailyTimeBudget both require same-day coverage.</summary>
    WrongDay,

    /// <summary>The referenced block's remaining deficit can't absorb this many minutes.</summary>
    ExceedsRemainingDeficit
}
