namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// The minimum total Focus hours required across a whole Nen Contract's period, as an
/// alternative to per-day floors (ContractDay.Floor) — used by Period Time Budget
/// contracts that opt out of a per-day obligation entirely in favor of a single
/// whole-period target. Same shape as DailyFloor (Hours + IsSatisfiedBy) but
/// deliberately not the same type: DailyFloor's upper bound of 24 hours is correct for
/// a single day and wrong for a period total, which can easily exceed it.
/// </summary>
public record ContractFloor
{
    public double Hours { get; }

    public ContractFloor(double hours)
    {
        if (hours < 0)
            throw new ArgumentException("Contract floor cannot be negative.");

        Hours = hours;
    }

    /// <summary>
    /// Pure numeric comparison used both at contract Seal (against total planned Focus
    /// hours across the period) and for the post-seal period-total-protection guard
    /// (against total currently-planned Focus hours after an edit).
    /// </summary>
    public bool IsSatisfiedBy(double totalFocusHours) => totalFocusHours >= Hours;
}
