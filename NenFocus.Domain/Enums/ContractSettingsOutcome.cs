namespace NenFocus.Domain.Enums;

/// <summary>
/// Whether a fixed daily floor and Work Hours can go together in a contract preset, or the first
/// reason not (Gap 92, V9) — <c>ContractPreset.CheckSettings</c>. A reason, never a sentence: the
/// wording is built in one place in the Application layer. "End after start" needs no reason here:
/// a <c>WorkHours</c> that breaks it can't be built.
/// </summary>
public enum ContractSettingsOutcome
{
    Allowed,

    /// <summary>A fixed floor is a whole number of hours from 1 to 24.</summary>
    FloorOutOfRange,

    /// <summary>The floor is longer than the Work Hours window, so no day could ever meet it.</summary>
    FloorLongerThanWorkHours
}
