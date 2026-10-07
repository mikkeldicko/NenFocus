using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// The answer to "may this change happen to this block?": allowed, or the first reason not —
/// with the day and the floor when a day would fall short, or the target when the whole period
/// would (Gap 93).
/// </summary>
public sealed record ChangeVerdict
{
    private ChangeVerdict(ChangeOutcome outcome, DateOnly? day = null, double? requiredHours = null)
    {
        Outcome = outcome;
        Day = day;
        RequiredHours = requiredHours;
    }

    public ChangeOutcome Outcome { get; }

    /// <summary>The day that would fall short — set exactly when <see cref="Outcome"/> is BelowDailyFloor.</summary>
    public DateOnly? Day { get; }

    /// <summary>The hours that would be missed — the day's floor for BelowDailyFloor, the whole-period target for BelowPeriodTarget.</summary>
    public double? RequiredHours { get; }

    public bool IsAllowed => Outcome == ChangeOutcome.Allowed;

    public static ChangeVerdict Allowed { get; } = new(ChangeOutcome.Allowed);

    public static ChangeVerdict Refused(ChangeOutcome outcome)
    {
        if (outcome is ChangeOutcome.Allowed or ChangeOutcome.BelowDailyFloor or ChangeOutcome.BelowPeriodTarget)
            throw new ArgumentException("Use Allowed, BelowDailyFloor or BelowPeriodTarget.", nameof(outcome));
        return new(outcome);
    }

    public static ChangeVerdict BelowDailyFloor(DateOnly day, double floorHours) =>
        new(ChangeOutcome.BelowDailyFloor, day, floorHours);

    public static ChangeVerdict BelowPeriodTarget(double targetHours) =>
        new(ChangeOutcome.BelowPeriodTarget, null, targetHours);
}
