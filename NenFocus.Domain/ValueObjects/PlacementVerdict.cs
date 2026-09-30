using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// What <see cref="Services.IBlockPlacementChecker"/> answers: allowed, or the first reason not,
/// with the time in the way when that reason is <see cref="PlacementOutcome.TimeTaken"/> (Gap 88).
/// </summary>
public sealed record PlacementVerdict
{
    private PlacementVerdict(PlacementOutcome outcome, TimeSlot? conflict)
    {
        if ((outcome == PlacementOutcome.TimeTaken) != (conflict is not null))
            throw new ArgumentException("A conflict is given exactly when the time is taken.", nameof(conflict));
        Outcome = outcome;
        Conflict = conflict;
    }

    public PlacementOutcome Outcome { get; }

    /// <summary>The time in the way — set exactly when <see cref="Outcome"/> is TimeTaken.</summary>
    public TimeSlot? Conflict { get; }

    public static PlacementVerdict Allowed { get; } = new(PlacementOutcome.Allowed, null);

    public static PlacementVerdict Refused(PlacementOutcome outcome, TimeSlot? conflict = null)
    {
        if (outcome == PlacementOutcome.Allowed)
            throw new ArgumentException("Use PlacementVerdict.Allowed.", nameof(outcome));
        return new(outcome, conflict);
    }
}
