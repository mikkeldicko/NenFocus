namespace NenFocus.Domain.Enums;

/// <summary>
/// The answer to "may a block go here?" (Gap 88). A block and a running session in the way are
/// the same outcome: both mean "pick another time", and a running planned session covers the
/// same time as its own block, so telling them apart would be arbitrary.
/// </summary>
public enum PlacementOutcome
{
    Allowed,
    InThePast,
    OutsideContractHours,
    TimeTaken,
}
