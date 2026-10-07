using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests;

/// <summary>
/// Memberships for tests. Only the Domain makes a <see cref="ContractMembership"/> (Gap 97), so
/// these go through production's doors where a public one exists: the drafting batch for Committed,
/// a contract for Bonus. Redemption comes only from <c>NenContract.PlaceRedemptionBlock</c> (Gap 106),
/// which needs a whole deficit set up, so tests use the internal constructor.
/// </summary>
internal static class TestContracts
{
    private static readonly DateTime Day = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>What a drafted, sealed block carries.</summary>
    public static ContractMembership Committed(Guid contractId) =>
        new ContractBlockBatchPreparer()
            .Prepare([(Guid.NewGuid(), At.Slot(Day, Day.AddHours(1)), true, null)], contractId, At.UtcZone)
            .Blocks[0].Membership!;

    /// <summary>What a block placed to cover a deficit carries.</summary>
    public static ContractMembership Redemption(Guid contractId) => new(contractId, ContractRole.Redemption);

    /// <summary>What any other block placed on a contract carries.</summary>
    public static ContractMembership Bonus(Guid contractId) =>
        Sealed(contractId).MembershipForPlacedBlock();

    private static NenContract Sealed(Guid contractId) => Seal(
        id: contractId,
        name: "Contract",
        goal: new NenContractGoal("Goal"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(DateOnly.FromDateTime(Day), DateOnly.FromDateTime(Day).AddDays(7), new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        allowCancellation: false,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(Day),
        timeZone: At.UtcZone);
    /// <summary>
    /// Seals a contract described the way most tests think of one — a strictness, a floor rule and
    /// a few flags — and builds the ContractRules the Domain takes (Gap 93). The floor rule is only
    /// read for a fixed floor: with none, the rules work it out from the strictness, as in production.
    /// </summary>
    public static NenContract Seal(
        Guid id,
        string name,
        NenContractGoal goal,
        StrictnessRule strictnessRule,
        ContractCalendar calendar,
        DailyFloorRule floorRule,
        bool allowCancellation,
        IReadOnlyDictionary<DateOnly, double> plannedFocusHoursByDay,
        Moment sealedAt,
        UserTimeZone timeZone,
        Guid? contractPresetId = null,
        RedemptionPlacementPolicy redemptionPlacementPolicy = RedemptionPlacementPolicy.AnywhereInContract,
        bool keepBlockingDuringBreaks = false)
        => NenContract.Seal(id, name, goal,
            TestRules.Of(strictnessRule, (floorRule as DailyFloorRule.Fixed)?.Floor, allowCancellation,
                keepBlockingDuringBreaks, redemptionPlacementPolicy),
            calendar, plannedFocusHoursByDay, sealedAt, timeZone, contractPresetId);
}