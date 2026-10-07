using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// A missed or late block of a contract that is still owed (Gap 106): what it failed to deliver
/// (<see cref="Owed"/>) and what is left of that once redemptions have paid
/// (<see cref="Remaining"/>, always above zero). Whole minutes (Gap 92, V10). Worked out by
/// <see cref="Entities.NenContract.Deficits"/>, never stored.
/// </summary>
public sealed record OutstandingDeficit(
    Guid MissedBlockId,
    Guid PresetId,
    TimeSlot Scheduled,
    FocusBlockOutcome Outcome,
    WholeMinutes Owed,
    WholeMinutes Remaining);
