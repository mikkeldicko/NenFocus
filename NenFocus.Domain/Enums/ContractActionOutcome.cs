namespace NenFocus.Domain.Enums;

/// <summary>
/// Whether a lifecycle action may happen to a Nen Contract now — edit, delete, complete early or
/// break — or the first reason it may not (Gaps 95, 92). A reason, never a sentence: the wording is
/// built in one place in the Application layer. Gap 105 adds the reasons for making or editing a
/// contract while a session runs.
/// </summary>
public enum ContractActionOutcome
{
    Allowed,

    /// <summary>Editing or deleting needs a contract that hasn't started: once Active it can only be completed or broken.</summary>
    NotScheduled,

    /// <summary>Completing needs a running (Active) contract.</summary>
    NotActive,

    /// <summary>Breaking needs a contract still in effect (Scheduled or Active).</summary>
    NotInEffect,

    /// <summary>A session is running: no contract ends while Focus time is still being worked.</summary>
    SessionIsRunning,

    /// <summary>The contract was sealed with cancellation off: it runs to its end.</summary>
    CancellationNotAllowed,

    /// <summary>Completing early needs every committed hour delivered first.</summary>
    HoursNotDelivered,

    /// <summary>Completing early needs every block of the contract to be done.</summary>
    BlocksStillPending
}
