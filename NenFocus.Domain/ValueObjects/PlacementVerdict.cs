using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// What <see cref="Services.BlockPlacementChecker"/> answers: allowed, or the first reason not,
/// with the time in the way when that reason is <see cref="PlacementOutcome.TimeTaken"/> (Gap 88).
/// </summary>
public sealed record PlacementVerdict
{
    private PlacementVerdict(PlacementOutcome outcome, TimeSlot? conflict, Guid? governingContractId = null)
    {
        if ((outcome == PlacementOutcome.TimeTaken) != (conflict is not null))
            throw new ArgumentException("A conflict is given exactly when the time is taken.", nameof(conflict));
        Outcome = outcome;
        Conflict = conflict;
        GoverningContractId = governingContractId;
    }

    public PlacementOutcome Outcome { get; }

    /// <summary>The time in the way — set exactly when <see cref="Outcome"/> is TimeTaken.</summary>
    public TimeSlot? Conflict { get; }

    /// <summary>
    /// The contract the allowed slot sits in, if any — placement and membership come from the
    /// same answer (Gap 97). Only ever set when <see cref="Outcome"/> is Allowed.
    /// </summary>
    public Guid? GoverningContractId { get; }

    /// <summary>Allowed, under no contract.</summary>
    public static PlacementVerdict Allowed { get; } = new(PlacementOutcome.Allowed, null);

    /// <summary>Allowed, inside <paramref name="governingContractId"/>'s period (none = <see cref="Allowed"/>).</summary>
    public static PlacementVerdict AllowedUnder(Guid? governingContractId) =>
        governingContractId is null ? Allowed : new(PlacementOutcome.Allowed, null, governingContractId);

    public static PlacementVerdict Refused(PlacementOutcome outcome, TimeSlot? conflict = null)
    {
        if (outcome == PlacementOutcome.Allowed)
            throw new ArgumentException("Use PlacementVerdict.Allowed.", nameof(outcome));
        return new(outcome, conflict);
    }
}
