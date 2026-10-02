namespace NenFocus.Domain.Enums;

/// <summary>
/// How far a block a contract binds may move — the one thing a StrictnessRule means, answered
/// by ContractRules.MovementRange (Gap 93). Always inside the contract's planned period.
/// </summary>
public enum MovementRange
{
    /// <summary>It stays where it was sealed.</summary>
    None,

    /// <summary>Anywhere on the day(s) it already sits on.</summary>
    OwnDay,

    /// <summary>Anywhere inside the contract's planned period.</summary>
    WholePeriod
}
