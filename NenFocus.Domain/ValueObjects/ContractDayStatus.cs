namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// One day of a Nen Contract as it stands (Gap 95, decisions 5 and 6): what it owes (its Committed
/// blocks' planned hours plus what the redemptions on it cover), what was done toward that, the
/// Bonus hours earned apart from it, and the verdict. Target 0 is a rest day. The verdict is worked
/// out from the hours, never stored beside them, so the two can't disagree (Gap 92, R1).
/// </summary>
public sealed record ContractDayStatus(DateOnly Date, double TargetHours, double CompletedHours, double BonusHours)
{
    /// <summary>A day that owes nothing.</summary>
    public bool IsRest => TargetHours <= 0;

    /// <summary>A rest day, or one whose completed hours reach what it owes.</summary>
    public bool IsMet => IsRest || CompletedHours >= TargetHours;

    /// <summary>Completed over target, uncapped (133% stays 133%); none on a rest day.</summary>
    public double? PercentMet => IsRest ? null : CompletedHours / TargetHours;
}
