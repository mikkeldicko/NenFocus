namespace NenFocus.Domain.Enums;

/// <summary>
/// The answer to "may a redemption block go here?" (Gap 106, decision 14). The refusals differ only
/// in what they tell the user to do, and are checked in this order, so one situation never gets two.
/// </summary>
public enum RedemptionPlacementOutcome
{
    Placed,
    OutsideContract,
    WrongDay,
    NothingOwed
}
