namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// How a Nen Contract's days get their floor when they are generated (Seal, an Edit
/// re-draft, a future Extend). A rule of the contract, like StrictnessRule — it never
/// answers "what is this day's floor"; only the day itself does (ContractDay.Floor).
/// Independent of StrictnessRule: Advanced contracts can pair any strictness with a
/// fixed floor or a plan-following one. Gap 65.
/// </summary>
public abstract record DailyFloorRule
{
    private DailyFloorRule() { }

    /// <summary>The same floor every day except rest days. A day whose window can't fit it is refused, never shrunk.
    /// Kept on the contract so a re-draft (Edit) knows the floor didn't come from the plan.</summary>
    public sealed record Fixed(DailyFloor Floor) : DailyFloorRule;

    /// <summary>Each day's floor is that day's planned Focus hours; a day with none planned is a rest day.</summary>
    public sealed record FollowsPlan : DailyFloorRule;

    /// <summary>No per-day obligation at all — a Period Time Budget contract held to a whole-period target (NenContract.HasPeriodTarget).</summary>
    public sealed record None : DailyFloorRule;
}
