using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests;

/// <summary>
/// Memberships for tests. Only the Domain makes a <see cref="ContractMembership"/> (Gap 97), so
/// these go through the same two doors production does: the drafting batch for Committed, a
/// contract for Redemption and Bonus.
/// </summary>
internal static class TestContracts
{
    private static readonly DateTime Day = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>What a drafted, sealed block carries.</summary>
    public static ContractMembership Committed(Guid contractId) =>
        new ContractBlockBatchPreparer(new PlannedFocusHoursCalculator())
            .Prepare([(Guid.NewGuid(), At.Slot(Day, Day.AddHours(1)), true, null)], contractId, At.UtcZone)
            .Blocks[0].Membership!;

    /// <summary>What a block placed to cover a deficit carries.</summary>
    public static ContractMembership Redemption(Guid contractId) =>
        Sealed(contractId).MembershipForPlacedBlock(claimsDeficit: true);

    /// <summary>What any other block placed on a contract carries.</summary>
    public static ContractMembership Bonus(Guid contractId) =>
        Sealed(contractId).MembershipForPlacedBlock(claimsDeficit: false);

    private static NenContract Sealed(Guid contractId) => NenContract.Seal(
        id: contractId,
        name: "Contract",
        goal: new NenContractGoal("Goal"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(DateOnly.FromDateTime(Day), DateOnly.FromDateTime(Day).AddDays(7), new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        changeWindow: null,
        allowCancellation: false,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(Day),
        timeZone: At.UtcZone);
}
