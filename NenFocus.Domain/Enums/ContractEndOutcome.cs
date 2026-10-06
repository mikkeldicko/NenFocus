namespace NenFocus.Domain.Enums;

/// <summary>
/// Whether a Nen Contract may be ended now — completed early or broken — or the first reason it
/// may not (Gap 95). A reason, never a sentence: the wording is built in one place in the
/// Application layer. Gap 105 adds the reasons for making or editing a contract.
/// </summary>
public enum ContractEndOutcome
{
    Allowed,

    /// <summary>A session is running: no contract ends while Focus time is still being worked.</summary>
    SessionIsRunning,

    /// <summary>Completing early needs every committed hour delivered first.</summary>
    HoursNotDelivered,

    /// <summary>Completing early needs every block of the contract to be done.</summary>
    BlocksStillPending
}
