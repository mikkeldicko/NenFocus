using NenFocus.Domain.Enums;
using NenFocus.Domain.Events;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

/// <summary>
/// A sealed, time-bound commitment over a contiguous date range. Does not own
/// PlannedBlocks — blocks remain their own aggregate and link back via the
/// nullable PlannedBlock.ContractId. The daily floor is a soft, tracked
/// invariant: enforcement and scoring are computed by Application-layer
/// guards that load this contract and the blocks separately.
/// </summary>
public class NenContract : AggregateRoot
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public NenContractGoal Goal { get; private set; } = null!;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public StrictnessRule StrictnessRule { get; private set; }
    public DailyFloor DailyFloor { get; private set; } = null!;
    public ChangeWindow? ChangeWindow { get; private set; }
    public bool AllowCancellation { get; private set; }
    public NenContractStatus Status { get; private set; }

    private NenContract() { } // EF Core

    /// <param name="plannedFocusHoursByDay">
    /// Focus block hours only, keyed by calendar day within [startDate, endDate].
    /// Background block hours must never be included — the daily floor is a
    /// Focus-only measure by design. Days missing from the map are treated as
    /// 0 planned hours. The caller (Application layer) owns assembling this map.
    /// </param>
    public static NenContract Seal(
        Guid id,
        string name,
        NenContractGoal goal,
        DateOnly startDate,
        DateOnly endDate,
        StrictnessRule strictnessRule,
        DailyFloor dailyFloor,
        ChangeWindow? changeWindow,
        bool allowCancellation,
        IReadOnlyDictionary<DateOnly, double> plannedFocusHoursByDay,
        DateTime sealedAt)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Contract name cannot be empty.", nameof(name));

        if (endDate < startDate)
            throw new ArgumentException("End date must be on or after the start date.");

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var plannedHours = plannedFocusHoursByDay.GetValueOrDefault(date);
            if (!dailyFloor.IsSatisfiedBy(plannedHours))
                throw new ArgumentException(
                    $"Day {date:yyyy-MM-dd} is planned below the daily floor of {dailyFloor.Hours}h (planned: {plannedHours}h).");
        }

        var contract = new NenContract
        {
            Id = id,
            Name = name.Trim(),
            Goal = goal,
            StartDate = startDate,
            EndDate = endDate,
            StrictnessRule = strictnessRule,
            DailyFloor = dailyFloor,
            ChangeWindow = changeWindow,
            AllowCancellation = allowCancellation,
            Status = DateOnly.FromDateTime(sealedAt) >= startDate
                ? NenContractStatus.Active
                : NenContractStatus.Scheduled
        };

        contract.Raise(new NenContractSealedEvent(id, contract.Name, startDate, endDate, sealedAt));

        return contract;
    }

    public void Activate(DateTime activatedAt)
    {
        if (Status != NenContractStatus.Scheduled)
            throw new InvalidOperationException("Only a scheduled contract can be activated.");

        Status = NenContractStatus.Active;
        Raise(new NenContractActivatedEvent(Id, activatedAt));
    }

    public void Complete(DateTime completedAt)
    {
        if (Status != NenContractStatus.Active)
            throw new InvalidOperationException("Only an active contract can be completed.");

        Status = NenContractStatus.Completed;
        Raise(new NenContractCompletedEvent(Id, completedAt));
    }

    public void Break(DateTime brokenAt)
    {
        if (Status != NenContractStatus.Active)
            throw new InvalidOperationException("Only an active contract can be broken.");

        if (!AllowCancellation)
            throw new InvalidOperationException("This contract does not allow cancellation.");

        Status = NenContractStatus.Broken;
        Raise(new NenContractBrokenEvent(Id, brokenAt));
    }

    /// <summary>
    /// A day is met if the daily floor is satisfied by that day's completed
    /// Focus hours AND no Focus block that day was missed.
    /// </summary>
    public bool IsDayMet(DayFocusOutcome outcome)
        => DailyFloor.IsSatisfiedBy(outcome.CompletedFocusHours) && !outcome.HasMissedFocusBlock;
}
