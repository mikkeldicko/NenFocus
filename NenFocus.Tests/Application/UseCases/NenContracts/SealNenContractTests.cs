using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NenFocus.Application.DTOs;
using NenFocus.Application.UseCases.NenContracts.Seal;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Persistence.Repositories;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

/// <summary>Sealing a contract, run as the app runs it: real pipeline, real SQLite (Gap 110).</summary>
public class SealNenContractTests : UseCaseTestBase
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);

    public SealNenContractTests() => Clock.SetNow(Now);

    private static NenContractBlockSpec Focus(DateTime start, DateTime end) =>
        new(SeededFocusPresetId, At.Slot(start, end), BlockPresetType.Focus);

    private static NenContractBlockSpec Focus(int day, int fromHour, int toHour) =>
        Focus(new DateTime(2026, 1, day, fromHour, 0, 0), new DateTime(2026, 1, day, toHour, 0, 0));

    /// <summary>A 1h Focus block (09:00–10:00) on every contract day but <paramref name="restDays"/> — meets a 1h fixed floor.</summary>
    private static IReadOnlyList<NenContractBlockSpec> OneHourEveryDayExcept(params DateOnly[] restDays) =>
        Enumerable.Range(0, EndDate.DayNumber - StartDate.DayNumber + 1)
            .Select(i => StartDate.AddDays(i))
            .Where(day => !restDays.Contains(day))
            .Select(day => Focus(day.ToDateTime(new TimeOnly(9, 0)), day.ToDateTime(new TimeOnly(10, 0))))
            .ToList();

    private static SealNenContractCommand Command(
        int? fixedFloor = null,
        IReadOnlyList<DateOnly>? restDays = null,
        IReadOnlyList<NenContractBlockSpec>? blocks = null) => new()
    {
        Name = "Focused Week",
        Goal = "Ship it",
        StartDate = StartDate,
        EndDate = EndDate,
        StrictnessRule = StrictnessRule.Locked,
        AllowCancellation = false,
        FixedDailyFloorHours = fixedFloor,
        RestDays = restDays ?? [],
        Blocks = blocks ?? []
    };

    /// <summary>Seals, expecting success, and reads back the stored contract.</summary>
    private async Task<NenContract> SealAsync(SealNenContractCommand command)
    {
        var result = await RunAsync(command);
        result.Success.Should().BeTrue(result.ErrorMessage);
        return (await FindAsync<NenContract>(result.ContractId!.Value))!;
    }

    /// <summary>Seals, expecting a refusal, and checks nothing was stored.</summary>
    private async Task<SealNenContractResult> RefusedAsync(SealNenContractCommand command)
    {
        var result = await RunAsync(command);
        result.Success.Should().BeFalse();
        (await ReadAsync(context => context.NenContracts.CountAsync())).Should().Be(0);
        return result;
    }

    // ── Sealed ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASimpleContract_IsStored_WithItsBlocksAsCommittedMembers()
    {
        var contract = await SealAsync(Command(blocks: [Focus(1, 9, 11)]));

        var members = await ReadAsync<IReadOnlyList<Block>>(async context =>
            await new BlockRepository(context).GetForContractAsync(contract.Id));
        members.Should().ContainSingle().Which.Membership.Should().Be(TestContracts.Committed(contract.Id));
    }

    [Fact]
    public async Task AnAdvancedContract_UsesItsFixedFloor_ExceptOnRestDays()
    {
        var restDay = StartDate.AddDays(2);

        var contract = await SealAsync(Command(fixedFloor: 1, restDays: [restDay], blocks: OneHourEveryDayExcept(restDay)));

        contract.FloorRule.Should().Be(new DailyFloorRule.Fixed(new DailyFloor(1)));
        contract.FloorHoursFor(StartDate).Should().Be(1);
        contract.FloorHoursFor(restDay).Should().BeNull();
    }

    [Fact]
    public async Task TheContractPreset_IsCarriedIntoTheContract()
    {
        (await SealAsync(Command() with { ContractPresetId = SeededContractPresetId })).ContractPresetId.Should().Be(SeededContractPresetId);
    }

    [Fact]
    public async Task WithoutWorkHours_EveryDayIsAFullDay()
    {
        var contract = await SealAsync(Command());

        contract.WindowOn(StartDate.AddDays(1)).Should().Be((
            StartDate.AddDays(1).ToDateTime(TimeOnly.MinValue), StartDate.AddDays(2).ToDateTime(TimeOnly.MinValue)));
    }

    [Fact]
    public async Task WithWorkHours_EveryDayIsTheWorkHoursWindow_ExceptNoWorkHoursDays()
    {
        var restDay = StartDate.AddDays(2);

        var contract = await SealAsync(Command() with
        {
            WorkHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            NoWorkHoursDays = [restDay]
        });

        contract.StartsAt.Should().Be(StartDate.ToDateTime(new TimeOnly(9, 0)));
        contract.EndsAt.Should().Be(EndDate.ToDateTime(new TimeOnly(17, 0)));
        contract.WindowOn(restDay).Should().Be((restDay.ToDateTime(TimeOnly.MinValue), restDay.AddDays(1).ToDateTime(TimeOnly.MinValue)));
    }

    [Fact]
    public async Task AFloorExactlyFillingTheWorkHours_IsAccepted()
    {
        var blocks = Enumerable.Range(0, EndDate.DayNumber - StartDate.DayNumber + 1)
            .Select(i => StartDate.AddDays(i))
            .Select(day => Focus(day.ToDateTime(new TimeOnly(9, 0)), day.ToDateTime(new TimeOnly(14, 0))))
            .ToList();

        await SealAsync(Command(fixedFloor: 5, blocks: blocks) with { WorkHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(14, 0)) });
    }

    [Fact]
    public async Task PeriodTimeBudgetWithoutAFixedFloor_TargetsTheTotalPlannedHours()
    {
        var contract = await SealAsync(Command(blocks: [Focus(1, 9, 12), Focus(2, 9, 13), Focus(3, 9, 11)])
            with { StrictnessRule = StrictnessRule.PeriodTimeBudget });

        contract.HasPeriodTarget.Should().BeTrue();
        contract.CommittedFocusHours.Should().Be(9); // 3 + 4 + 2 — the whole-period target
        contract.FloorRule.Should().Be(new DailyFloorRule.None());
        contract.FloorHoursFor(StartDate).Should().BeNull();
    }

    [Fact]
    public async Task PeriodTimeBudgetWithAFixedFloor_UsesTheDailyFloor_NoPeriodTarget()
    {
        var contract = await SealAsync(Command(fixedFloor: 1, blocks: OneHourEveryDayExcept()) with { StrictnessRule = StrictnessRule.PeriodTimeBudget });

        contract.HasPeriodTarget.Should().BeFalse();
        contract.FloorRule.Should().Be(new DailyFloorRule.Fixed(new DailyFloor(1)));
    }

    [Fact]
    public async Task EveryRuleOfTheCommand_IsCarriedIntoTheContract()
    {
        // The rules travel as one value (Gap 93), so Seal can't drop one of them — keep-blocking
        // during breaks included (Gap 64, trap 2: Seal was never given it).
        var contract = await SealAsync(Command() with
        {
            StrictnessRule = StrictnessRule.DailyFlexibility,
            AllowCancellation = true,
            KeepBlockingDuringBreaks = true,
            RedemptionPlacementPolicy = RedemptionPlacementPolicy.CurrentDayOnly,
            BonusBlocksFollowStrictness = true
        });

        contract.Rules.Should().Be(new ContractRules(
            StrictnessRule.DailyFlexibility, fixedDailyFloor: null, allowCancellation: true, keepBlockingDuringBreaks: true,
            RedemptionPlacementPolicy.CurrentDayOnly, bonusBlocksFollowStrictness: true));
    }

    // ── Gap 65: the start moment ───────────────────────────────────────────────────────

    [Fact]
    public async Task StartingTodayAfterWorkHoursOpened_StartsAtTheCurrentMinute_WithTheFullFloor()
    {
        Clock.SetNow(StartDate.ToDateTime(new TimeOnly(12, 0, 40)));
        // 4h a day meets each day's floor; today's block starts after now, inside the hours left.
        var blocks = Enumerable.Range(0, EndDate.DayNumber - StartDate.DayNumber + 1)
            .Select(i =>
            {
                var from = StartDate.AddDays(i).ToDateTime(i == 0 ? new TimeOnly(12, 30) : new TimeOnly(9, 0));
                return Focus(from, from.AddHours(4));
            })
            .ToList();

        var contract = await SealAsync(Command(fixedFloor: 4, blocks: blocks) with { WorkHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)) });

        contract.StartsAt.Should().Be(StartDate.ToDateTime(new TimeOnly(12, 0)));
        contract.Status.Should().Be(NenContractStatus.Active);
        contract.FloorHoursFor(StartDate).Should().Be(4); // the full floor — 5h are left, never shrunk
    }

    // ── Refused, and nothing is stored ─────────────────────────────────────────────────

    [Fact]
    public async Task AStartDateAlreadyPast_IsRefused()
    {
        var result = await RefusedAsync(Command() with { StartDate = StartDate.AddDays(-1) });

        result.ErrorMessage.Should().Contain("start time has passed");
    }

    [Fact]
    public async Task StartingTodayWithLessTimeLeftThanTheFloor_IsRefused()
    {
        // The reported case: 16:38 with Work Hours until 18:00 — 1h22m left for a 2h floor.
        Clock.SetNow(StartDate.ToDateTime(new TimeOnly(16, 38)));

        var result = await RefusedAsync(Command(fixedFloor: 2) with { WorkHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(18, 0)) });

        result.ErrorMessage.Should().StartWith("Not enough time left on");
    }

    [Fact]
    public async Task ADayBelowTheFixedFloor_IsRefused()
    {
        var result = await RefusedAsync(Command(fixedFloor: 4));

        result.ErrorMessage.Should().Contain("below the daily floor");
    }

    [Fact]
    public async Task TwoOfItsFocusBlocksOverlapping_AreRefused()
    {
        await RefusedAsync(Command(blocks: [Focus(1, 9, 11), Focus(1, 10, 12)]));
    }

    [Fact]
    public async Task AFocusBlockOverlappingAStoredOne_IsRefused()
    {
        await SeedAsync(FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 1, 10, 0, 0), new DateTime(2026, 1, 1, 12, 0, 0))));

        await RefusedAsync(Command(blocks: [Focus(1, 9, 11)]));
    }

    [Fact]
    public async Task AFocusBlockOverlappingTheRunningSession_IsRefused()
    {
        // Gap 88: Seal respects a running session too (BlockPlacementChecker.CheckFree).
        Clock.SetNow(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        var running = Session.Create(Guid.NewGuid(), new SessionGoal("Focus"),
            new SessionDuration(TimeSpan.FromHours(1)), Guid.NewGuid(), SessionOrigin.Spontaneous);
        running.Start(At.Utc(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc)));
        await SeedAsync(running);

        await RefusedAsync(Command(blocks: [Focus(1, 10, 12)]));
    }

    [Fact]
    public async Task ABlockOutsideItsDaysWindow_IsRefused()
    {
        await RefusedAsync(Command(blocks: [Focus(1, 18, 19)]) with { WorkHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)) });
    }

    [Fact]
    public async Task ABlankName_IsRefused() => await RefusedAsync(Command() with { Name = "  " });

    [Fact]
    public async Task EndingBeforeItStarts_IsRefused() => await RefusedAsync(Command() with { StartDate = EndDate, EndDate = StartDate });

    [Theory]
    [InlineData(-1)]
    [InlineData(0)] // zero would mean "no floor" by another name — leave it unset instead
    [InlineData(25)]
    public async Task AFixedFloorOutOfRange_IsRefused(int floor) => await RefusedAsync(Command() with { FixedDailyFloorHours = floor });

    [Fact]
    public async Task AFixedFloorLongerThanTheWorkHours_IsRefused() =>
        await RefusedAsync(Command() with
        {
            FixedDailyFloorHours = 6,
            WorkHours = new WorkHours(new TimeOnly(10, 0), new TimeOnly(15, 0)) // 5h window — a 6h floor could never fit
        });

    [Fact]
    public async Task AnUnknownContractPreset_IsRefused() => await RefusedAsync(Command() with { ContractPresetId = Guid.NewGuid() });

    [Fact]
    public async Task AnUnknownBlockPreset_IsRefused() =>
        await RefusedAsync(Command(blocks: [new NenContractBlockSpec(Guid.NewGuid(), At.Slot(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0)), BlockPresetType.Focus)]));

    [Fact]
    public async Task DatesOverlappingAnotherContract_AreRefused()
    {
        await SeedAsync(TestContracts.Seal(
            id: Guid.NewGuid(), name: "Other Contract", goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(EndDate, EndDate.AddDays(5), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(), allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Now.AddDays(-1)), timeZone: At.UtcZone));

        var result = await RunAsync(Command());

        result.Success.Should().BeFalse();
        (await ReadAsync(context => context.NenContracts.CountAsync())).Should().Be(1);
    }
}
