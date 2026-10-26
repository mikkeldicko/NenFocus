using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.UseCases.NenContracts.Edit;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

/// <summary>
/// Editing a contract, run as the app runs it: real pipeline, real SQLite (Gap 110). Only a contract
/// that hasn't started may be edited; it is re-sealed under the same id, its old blocks replaced.
/// </summary>
public class EditNenContractTests : UseCaseTestBase
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 6, 1);
    private static readonly DateOnly EndDate = new(2026, 6, 7);

    public EditNenContractTests() => Clock.SetNow(Now);

    private static NenContract Contract(DateOnly sealedOn, DateOnly? start = null, DateOnly? end = null) => TestContracts.Seal(
        id: Guid.NewGuid(),
        name: "Original Name",
        goal: new NenContractGoal("Original goal"),
        strictnessRule: StrictnessRule.DailyFlexibility,
        calendar: TestCalendars.For(start ?? StartDate, end ?? EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(sealedOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    private static NenContract ScheduledContract() => Contract(sealedOn: StartDate.AddDays(-10));

    private static TimeSlot On(int day, int fromHour, int toHour) =>
        At.Slot(StartDate.AddDays(day).ToDateTime(new TimeOnly(fromHour, 0)), StartDate.AddDays(day).ToDateTime(new TimeOnly(toHour, 0)));

    private static NenContractBlockSpec Focus(int day, int fromHour, int toHour) => new(SeededFocusPresetId, On(day, fromHour, toHour), IsFocus: true);

    private static EditNenContractCommand Command(Guid contractId, params NenContractBlockSpec[] blocks) => new()
    {
        ContractId = contractId,
        Name = "Renamed Contract",
        Goal = "Updated goal",
        StartDate = StartDate,
        EndDate = EndDate,
        StrictnessRule = StrictnessRule.Locked,
        AllowCancellation = false,
        Blocks = blocks
    };

    private async Task<NenContract> StoredAsync(NenContract contract) => (await FindAsync<NenContract>(contract.Id))!;

    private Task<IReadOnlyList<Block>> MembersAsync(Guid contractId) =>
        ReadAsync<IReadOnlyList<Block>>(async context =>
            await new NenFocus.Infrastructure.Persistence.Repositories.BlockRepository(context).GetForContractAsync(contractId));

    // ── A valid edit ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AValidEdit_ReSealsUnderTheSameId_ReplacingTheOldBlocks()
    {
        var contract = ScheduledContract();
        var oldFocus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), On(0, 9, 10), membership: TestContracts.Committed(contract.Id));
        var oldBackground = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), On(0, 11, 12), membership: TestContracts.Committed(contract.Id));
        await SeedAsync(contract, oldFocus, oldBackground);

        var result = await RunAsync(Command(contract.Id, Focus(1, 9, 10)));

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.ContractId.Should().Be(contract.Id);
        (await StoredAsync(contract)).Name.Should().Be("Renamed Contract");
        (await FindAsync<Block>(oldFocus.Id)).Should().BeNull();
        (await FindAsync<Block>(oldBackground.Id)).Should().BeNull();
        var members = await MembersAsync(contract.Id);
        members.Should().ContainSingle().Which.Scheduled.Should().Be(On(1, 9, 10));
        members[0].Membership.Should().Be(TestContracts.Committed(contract.Id));
    }

    [Fact]
    public async Task ReusingTheSlotsOfItsOwnOldBlocks_IsNotACollision()
    {
        var contract = ScheduledContract();
        await SeedAsync(contract, FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), On(0, 9, 10), membership: TestContracts.Committed(contract.Id)));

        var result = await RunAsync(Command(contract.Id, Focus(0, 9, 10)));

        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task TheContractPreset_IsCarriedIntoTheReSealedContract()
    {
        var contract = ScheduledContract();
        await SeedAsync(contract);

        await RunAsync(Command(contract.Id) with { ContractPresetId = SeededContractPresetId });

        (await StoredAsync(contract)).ContractPresetId.Should().Be(SeededContractPresetId);
    }

    [Fact]
    public async Task PeriodTimeBudgetWithoutAFixedFloor_TargetsTheTotalPlannedHours()
    {
        var contract = ScheduledContract();
        await SeedAsync(contract);

        var result = await RunAsync(Command(contract.Id, Focus(0, 9, 12), Focus(1, 9, 13), Focus(2, 9, 11))
            with { StrictnessRule = StrictnessRule.PeriodTimeBudget });

        result.Success.Should().BeTrue(result.ErrorMessage);
        var stored = await StoredAsync(contract);
        stored.HasPeriodTarget.Should().BeTrue();
        stored.CommittedFocusHours.Should().Be(9); // 3 + 4 + 2 — the whole-period target
        stored.FloorRule.Should().Be(new DailyFloorRule.None());
    }

    [Fact]
    public async Task EveryRuleOfTheCommand_IsCarriedIntoTheReSealedContract()
    {
        // The rules travel as one value (Gap 93), so a re-seal can't drop one of them — including
        // keep-blocking during breaks (Gap 64, trap 3: a re-seal used to drop it).
        var contract = ScheduledContract();
        await SeedAsync(contract);
        var command = Command(contract.Id) with
        {
            AllowCancellation = true,
            KeepBlockingDuringBreaks = true,
            RedemptionPlacementPolicy = RedemptionPlacementPolicy.CurrentDayOnly,
            BonusBlocksFollowStrictness = true
        };

        var result = await RunAsync(command);

        result.Success.Should().BeTrue(result.ErrorMessage);
        (await StoredAsync(contract)).Rules.Should().Be(new ContractRules(
            command.StrictnessRule, null,
            allowCancellation: true, keepBlockingDuringBreaks: true,
            RedemptionPlacementPolicy.CurrentDayOnly, bonusBlocksFollowStrictness: true));
    }

    // ── Refused, and nothing changes ───────────────────────────────────────────────────

    private async Task RefusedAndUnchangedAsync(NenContract contract, EditNenContractCommand command, string? reason = null)
    {
        var result = await RunAsync(command);

        result.Success.Should().BeFalse();
        if (reason is not null) result.ErrorMessage.Should().Contain(reason);
        (await StoredAsync(contract)).Name.Should().Be("Original Name");
    }

    [Fact]
    public async Task ContractNotFound_Fails()
    {
        var result = await RunAsync(Command(Guid.NewGuid()));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task AnActiveContract_IsRefused()
    {
        var contract = Contract(sealedOn: StartDate); // Active from its first day
        await SeedAsync(contract);

        await RefusedAndUnchangedAsync(contract, Command(contract.Id));
    }

    [Fact]
    public async Task TwoOfItsFocusBlocksOverlapping_IsRefused()
    {
        var contract = ScheduledContract();
        await SeedAsync(contract);

        await RefusedAndUnchangedAsync(contract, Command(contract.Id, Focus(0, 9, 11), Focus(0, 10, 12)));
    }

    [Fact]
    public async Task OverlappingAPlainFocusBlock_IsRefused_AndItsOldBlocksStay()
    {
        // Contracts never overlap, so the Focus block in the way is a plain one already on the calendar.
        var contract = ScheduledContract();
        var ownOld = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), On(1, 9, 10), membership: TestContracts.Committed(contract.Id));
        var plain = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            At.Slot(StartDate.ToDateTime(new TimeOnly(9, 30)), StartDate.ToDateTime(new TimeOnly(10, 30))));
        await SeedAsync(contract, ownOld, plain);

        await RefusedAndUnchangedAsync(contract, Command(contract.Id, Focus(0, 9, 10)));
        (await FindAsync<Block>(ownOld.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task ADayBelowTheFixedFloor_IsRefused()
    {
        var contract = ScheduledContract();
        await SeedAsync(contract);

        await RefusedAndUnchangedAsync(contract, Command(contract.Id, Focus(0, 9, 10)) with { FixedDailyFloorHours = 4 }, "below the daily floor");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)] // zero would mean "no floor" by another name — leave it unset instead
    public async Task AFixedFloorOutOfRange_IsRefused(int floor)
    {
        var contract = ScheduledContract();
        await SeedAsync(contract);

        await RefusedAndUnchangedAsync(contract, Command(contract.Id) with { FixedDailyFloorHours = floor });
    }

    [Fact]
    public async Task AFixedFloorLongerThanTheWorkHours_IsRefused()
    {
        var contract = ScheduledContract();
        await SeedAsync(contract);

        await RefusedAndUnchangedAsync(contract, Command(contract.Id) with
        {
            FixedDailyFloorHours = 6,
            WorkHours = new WorkHours(new TimeOnly(10, 0), new TimeOnly(15, 0))
        });
    }

    [Fact]
    public async Task AnEmptyName_IsRefused()
    {
        var contract = ScheduledContract();
        await SeedAsync(contract);

        await RefusedAndUnchangedAsync(contract, Command(contract.Id) with { Name = "" });
    }

    [Fact]
    public async Task EndingBeforeItStarts_IsRefused()
    {
        var contract = ScheduledContract();
        await SeedAsync(contract);

        await RefusedAndUnchangedAsync(contract, Command(contract.Id) with { StartDate = EndDate, EndDate = StartDate });
    }

    [Fact]
    public async Task AnUnknownContractPreset_IsRefused()
    {
        var contract = ScheduledContract();
        await SeedAsync(contract);

        await RefusedAndUnchangedAsync(contract, Command(contract.Id) with { ContractPresetId = Guid.NewGuid() });
    }

    [Fact]
    public async Task AnUnknownBlockPreset_IsRefused()
    {
        var contract = ScheduledContract();
        await SeedAsync(contract);

        await RefusedAndUnchangedAsync(contract, Command(contract.Id, new NenContractBlockSpec(Guid.NewGuid(), On(0, 9, 10), IsFocus: true)));
    }

    [Fact]
    public async Task DatesOverlappingAnotherContract_AreRefused_ButItsOwnDatesAreNot()
    {
        var contract = ScheduledContract();
        var other = Contract(sealedOn: StartDate.AddDays(-10), start: EndDate.AddDays(-1), end: EndDate.AddDays(5));
        await SeedAsync(contract, other);

        await RefusedAndUnchangedAsync(contract, Command(contract.Id));
        (await RunAsync(Command(contract.Id) with { EndDate = EndDate.AddDays(-2) })).Success.Should().BeTrue("its own old dates never collide");
    }
}
