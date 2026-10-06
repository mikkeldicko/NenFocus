namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// One day of a Nen Contract as it stands (Gap 95, decisions 5 and 6): what it owes (its Committed
/// blocks' planned hours plus what the redemptions on it cover), what was done toward that, the
/// Bonus hours earned apart from it, and the verdict. Target 0 is a rest day.
/// </summary>
public sealed record ContractDayStatus(
    DateOnly Date, double TargetHours, double CompletedHours, double BonusHours, bool IsRest, bool IsMet)
{
    /// <summary>Completed over target, uncapped (133% stays 133%); none on a rest day.</summary>
    public double? PercentMet => IsRest ? null : CompletedHours / TargetHours;
}
