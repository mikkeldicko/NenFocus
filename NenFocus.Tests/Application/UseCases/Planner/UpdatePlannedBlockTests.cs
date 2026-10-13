using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.Planner;

/// <summary>
/// Moving, resizing or re-presetting a planned block, run as the app runs it: real pipeline, real
/// SQLite (Gap 110). May it change? BlockChangeChecker.CheckReschedule (Gap 93) — the block's own
/// lock, then its contract's rules, then the floor — asked before placement, whose every case is in
/// GetBlockPlacementVerdictQueryHandlerTests. A move never changes membership, and a redemption
/// block keeps its receipt (Gap 106).
/// </summary>
public class UpdatePlannedBlockTests : UseCaseTestBase
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public UpdatePlannedBlockTests() => Clock.SetNow(Now);

    private static UpdatePlannedBlockCommand Command(PlannedBlock block, DateTime from, DateTime to, Guid? presetId = null) => new()
    {
        BlockId = block.Id,
        PresetId = presetId ?? block.BlockPresetId,
        Slot = At.Slot(from, to)
    };

    private Task<UpdatePlannedBlockResult> UpdateAsync(UpdatePlannedBlockCommand command) => RunAsync(command);

    private static FocusBlock Focus(DateTime from, DateTime to, ContractMembership? membership = null) =>
        FocusBlock.Create(Guid.NewGuid(), SeededFocusPresetId, At.Slot(from, to), membership: membership);

    private static BackgroundBlock Background(DateTime from, DateTime to) =>
        BackgroundBlock.Create(Guid.NewGuid(), SeededBackgroundPresetId, At.Slot(from, to));

    private async Task<PlannedBlock> Stored(PlannedBlock block) => (await FindAsync<PlannedBlock>(block.Id))!;

    private async Task ShouldBeAllowed(UpdatePlannedBlockCommand command)
    {
        var result = await UpdateAsync(command);

        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    /// <summary>Refused, and the stored block is as it was.</summary>
    private async Task<UpdatePlannedBlockResult> ShouldBeRefused(PlannedBlock block, UpdatePlannedBlockCommand command, string? message = null)
    {
        var before = block.Scheduled;
        var presetBefore = block.BlockPresetId;

        var result = await UpdateAsync(command);

        result.Success.Should().BeFalse();
        if (message is not null) result.ErrorMessage.Should().Be(message);
        var stored = await Stored(block);
        stored.Scheduled.Should().Be(before);
        stored.BlockPresetId.Should().Be(presetBefore);
        return result;
    }

    private async Task<Guid> AnotherFocusPreset()
    {
        var preset = BlockPreset.Create(Guid.NewGuid(), "Other", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Now));
        await SeedAsync(preset);
        return preset.Id;
    }

    // ── What gets stored ─────────────────────────────────────────────────────

    [Fact]
    public async Task AFutureBlock_IsRescheduledAndRepreseted()
    {
        var block = Focus(Now.AddHours(1), Now.AddHours(2));
        var newPresetId = await AnotherFocusPreset();
        await SeedAsync(block);

        await ShouldBeAllowed(Command(block, Now.AddHours(3), Now.AddHours(5), newPresetId));

        var stored = await Stored(block);
        stored.Scheduled.Should().Be(At.Slot(Now.AddHours(3), Now.AddHours(5)));
        stored.BlockPresetId.Should().Be(newPresetId);
    }

    [Fact]
    public async Task ABackgroundBlock_IsRescheduled()
    {
        var block = Background(Now.AddHours(1), Now.AddHours(2));
        await SeedAsync(block);

        await ShouldBeAllowed(Command(block, Now.AddHours(3), Now.AddHours(4)));

        (await Stored(block)).Scheduled.Should().Be(At.Slot(Now.AddHours(3), Now.AddHours(4)));
    }

    [Fact]
    public async Task AnUnknownBlock_IsRefused()
    {
        var result = await UpdateAsync(Command(Focus(Now.AddHours(1), Now.AddHours(2)), Now.AddHours(3), Now.AddHours(4)));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task AnUnknownPreset_IsRefused()
    {
        var block = Focus(Now.AddHours(1), Now.AddHours(2));
        await SeedAsync(block);

        await ShouldBeRefused(block, Command(block, Now.AddHours(1), Now.AddHours(2), presetId: Guid.NewGuid()));
    }

    // ── Form ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnEmptyBlockId_IsRefused()
    {
        var result = await UpdateAsync(Command(Focus(Now.AddHours(1), Now.AddHours(2)), Now.AddHours(3), Now.AddHours(4)) with { BlockId = Guid.Empty });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task AnEmptyPresetId_IsRefused()
    {
        var block = Focus(Now.AddHours(1), Now.AddHours(2));
        await SeedAsync(block);

        await ShouldBeRefused(block, Command(block, Now.AddHours(1), Now.AddHours(2)) with { PresetId = Guid.Empty });
    }

    [Fact]
    public async Task APresetOfTheOtherType_IsRefused()
    {
        var block = Focus(Now.AddHours(1), Now.AddHours(2));
        await SeedAsync(block);

        await ShouldBeRefused(block, Command(block, Now.AddHours(1), Now.AddHours(2), presetId: SeededBackgroundPresetId),
            "The selected preset type does not match this block preset type.");
    }

    [Fact]
    public async Task AFocusBlockOverItsMaxDuration_IsRefused()
    {
        var block = Focus(Now.AddHours(1), Now.AddHours(2));
        await SeedAsync(block);

        await ShouldBeRefused(block, Command(block, Now.AddHours(1), Now.AddHours(1 + FocusBlock.MaxDurationHours).AddMinutes(1)),
            $"A Focus block cannot exceed {FocusBlock.MaxDurationHours} hours.");
    }

    [Fact]
    public async Task AFocusBlockAtExactlyItsMaxDuration_IsAllowed()
    {
        var block = Focus(Now.AddHours(1), Now.AddHours(2));
        await SeedAsync(block);

        await ShouldBeAllowed(Command(block, Now.AddHours(1), Now.AddHours(1 + FocusBlock.MaxDurationHours)));
    }

    [Fact]
    public async Task ABackgroundBlockLongerThanTheFocusMax_IsAllowed()
    {
        var block = Background(Now.AddHours(1), Now.AddHours(2));
        await SeedAsync(block);

        await ShouldBeAllowed(Command(block, Now.AddHours(1), Now.AddHours(1 + FocusBlock.MaxDurationHours).AddDays(1)));
    }

    // ── The block's own lock, asked before placement ─────────────────────────

    [Fact]
    public async Task AnEndedBlock_IsRefused()
    {
        // A block whose window has closed (e.g. Missed) is a historical record.
        var block = Focus(Now.AddHours(-3), Now.AddHours(-2));
        await SeedAsync(block);

        await ShouldBeRefused(block, Command(block, Now.AddHours(-3), Now.AddHours(-1)));
    }

    [Fact]
    public async Task AStartedBlock_IsRefusedAsStarted_NotAsInThePast()
    {
        // Gap 88 phase 0: not even its end may change. Permission is asked first, so the answer is
        // "it has started", not placement's "that time is in the past".
        var block = Focus(Now.AddMinutes(-30), Now.AddHours(1));
        await SeedAsync(block);

        await ShouldBeRefused(block, Command(block, Now.AddMinutes(-30), Now.AddHours(2)),
            "This block has already started — it cannot be changed.");
    }

    [Fact]
    public async Task ABlockStartingExactlyNow_IsRefusedAsStarted()
    {
        var block = Background(Now, Now.AddHours(1));
        await SeedAsync(block);

        await ShouldBeRefused(block, Command(block, Now.AddHours(1), Now.AddHours(2)),
            "This block has already started — it cannot be changed.");
    }

    // ── Placement: the query's refusal reaches the command ──────────────────

    [Fact]
    public async Task OntoAnotherFocusBlock_IsRefused()
    {
        var block = Focus(Now.AddHours(1), Now.AddHours(2));
        await SeedAsync(block, Focus(Now.AddHours(3), Now.AddHours(4)));

        await ShouldBeRefused(block, Command(block, Now.AddHours(3), Now.AddHours(4)),
            "A Focus block already exists in this time window.");
    }

    [Fact]
    public async Task OverlappingItsOwnOldSlot_IsAllowed()
    {
        // Placement is asked as an edit of this block, so it is never in its own way.
        var block = Focus(Now.AddHours(1), Now.AddHours(2));
        await SeedAsync(block);

        await ShouldBeAllowed(Command(block, Now.AddHours(1).AddMinutes(30), Now.AddHours(2).AddMinutes(30)));
    }

    // ── A contract's rules ───────────────────────────────────────────────────

    /// <summary>A contract over today and the next seven days, Active on seal.</summary>
    private static NenContract Contract(StrictnessRule rule, double? floorHours = null)
    {
        var floor = floorHours is { } hours ? new DailyFloor(hours) : null;
        var rules = TestRules.Of(rule, floor);
        return NenContract.Seal(Guid.NewGuid(), "Contract", new NenContractGoal("Goal"), rules,
            TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7), rules.FloorRule),
            Enumerable.Range(0, 8).ToDictionary(i => DateOnly.FromDateTime(Now).AddDays(i), _ => floorHours ?? 0),
            At.Utc(Now.Date), At.UtcZone);
    }

    private async Task<NenContract> ContractAsync(StrictnessRule rule, double? floorHours = null)
    {
        var contract = Contract(rule, floorHours);
        await SeedAsync(contract);
        return contract;
    }

    private async Task<FocusBlock> CommittedBlockAsync(StrictnessRule rule)
    {
        var contract = await ContractAsync(rule);
        var block = Focus(Now.AddHours(1), Now.AddHours(2), TestContracts.Committed(contract.Id));
        await SeedAsync(block);
        return block;
    }

    [Fact]
    public async Task UnderLockdown_AMove_IsRefused()
    {
        var block = await CommittedBlockAsync(StrictnessRule.Locked);

        await ShouldBeRefused(block, Command(block, Now.AddHours(3), Now.AddHours(4)),
            "This block is locked by a Lockdown contract.");
    }

    [Fact]
    public async Task UnderLockdown_SavingWithNothingChanged_IsAllowed()
    {
        // Only what changes is asked about (Gap 93) — an untouched form is not a move.
        var block = await CommittedBlockAsync(StrictnessRule.Locked);

        await ShouldBeAllowed(Command(block, Now.AddHours(1), Now.AddHours(2)));
    }

    [Fact]
    public async Task UnderDailyFlexibility_ASameDayMove_IsAllowed_AndKeepsItsMembership()
    {
        // Before Gap 97 an edit recomputed attribution — committed hours could turn into redemption coverage.
        var block = await CommittedBlockAsync(StrictnessRule.DailyFlexibility);

        await ShouldBeAllowed(Command(block, Now.AddHours(3), Now.AddHours(4)));

        var stored = (FocusBlock)await Stored(block);
        stored.Membership.Should().Be(block.Membership);
        stored.Receipt.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task UnderDailyFlexibility_AMoveEndingExactlyAtMidnight_StaysOnItsDay()
    {
        // 22:00–24:00 ends on the day boundary but never crosses it.
        var contract = await ContractAsync(StrictnessRule.DailyFlexibility);
        var day = Now.Date.AddDays(2);
        var block = Focus(day.AddHours(14), day.AddHours(16), TestContracts.Committed(contract.Id));
        await SeedAsync(block);

        await ShouldBeAllowed(Command(block, day.AddHours(22), day.AddHours(24)));
    }

    [Fact]
    public async Task UnderDailyFlexibility_AMoveToAnotherDay_IsRefused()
    {
        var block = await CommittedBlockAsync(StrictnessRule.DailyFlexibility);

        await ShouldBeRefused(block, Command(block, Now.AddDays(1).AddHours(1), Now.AddDays(1).AddHours(2)));
    }

    [Fact]
    public async Task UnderPeriodTimeBudget_AMoveWithinThePeriod_IsAllowed()
    {
        var block = await CommittedBlockAsync(StrictnessRule.PeriodTimeBudget);

        await ShouldBeAllowed(Command(block, Now.AddDays(3), Now.AddDays(3).AddHours(1)));
    }

    [Fact]
    public async Task UnderPeriodTimeBudget_AMoveOutOfThePeriod_IsRefused()
    {
        var block = await CommittedBlockAsync(StrictnessRule.PeriodTimeBudget);

        await ShouldBeRefused(block, Command(block, Now.AddDays(10), Now.AddDays(10).AddHours(1)));
    }

    [Fact]
    public async Task ACommittedBlocksPresetChange_IsRefused_EvenWithoutATimeChange()
    {
        var block = await CommittedBlockAsync(StrictnessRule.PeriodTimeBudget);

        await ShouldBeRefused(block, Command(block, Now.AddHours(1), Now.AddHours(2), presetId: await AnotherFocusPreset()));
    }

    [Fact]
    public async Task AFreeBonusBlock_MayChangeItsPreset_EvenUnderLockdown()
    {
        var contract = await ContractAsync(StrictnessRule.Locked);
        var block = Focus(Now.AddHours(1), Now.AddHours(2), contract.MembershipForPlacedBlock());
        await SeedAsync(block);
        var newPresetId = await AnotherFocusPreset();

        await ShouldBeAllowed(Command(block, Now.AddHours(1), Now.AddHours(2), newPresetId));

        (await Stored(block)).BlockPresetId.Should().Be(newPresetId);
    }

    [Fact]
    public async Task ABonusBlockMovedOutOfItsContract_IsRefused_AndStaysItsContractsBlock()
    {
        // Gap 93: a member never leaves its contract's period by a move; Gap 106: nor does a move
        // ever end a membership.
        var contract = await ContractAsync(StrictnessRule.PeriodTimeBudget);
        var block = Focus(Now.AddHours(1), Now.AddHours(2), contract.MembershipForPlacedBlock());
        await SeedAsync(block);

        await ShouldBeRefused(block, Command(block, Now.AddDays(20), Now.AddDays(20).AddHours(1)));

        (await Stored(block)).Membership.Should().Be(TestContracts.Bonus(contract.Id));
    }

    [Fact]
    public async Task APlainBlockMovedIntoAContractInEffect_IsRefused_ADragNeverJoins()
    {
        // Gap 106, decision 18: the validator hands the checker every contract in effect.
        await ContractAsync(StrictnessRule.PeriodTimeBudget); // today + 7 days
        var block = Focus(Now.AddDays(10), Now.AddDays(10).AddHours(1));
        await SeedAsync(block);

        await ShouldBeRefused(block, Command(block, Now.AddHours(1), Now.AddHours(2)),
            BlockChangeVerdictDto.From(ChangeVerdict.Refused(ChangeOutcome.WouldJoinContract)).Message);

        (await Stored(block)).Membership.Should().BeNull();
    }

    [Fact]
    public async Task AFinishedContractsBlock_IsHistory()
    {
        var contract = Contract(StrictnessRule.Locked);
        contract.Complete(At.Utc(Now.AddDays(8)));
        var block = Focus(Now.AddHours(1), Now.AddHours(2), TestContracts.Committed(contract.Id));
        await SeedAsync(contract, block);

        var result = await ShouldBeRefused(block, Command(block, Now.AddHours(3), Now.AddHours(4)));

        result.ErrorMessage.Should().Contain("finished Nen Contract");
    }

    [Fact]
    public async Task ARedemptionBlockMoved_KeepsItsReceipt()
    {
        // Gap 106, decision 3: a redemption block's claims are set once, when it is placed; a move
        // keeps them (and its length — Gap 95, decision 16), even when the debt has since shrunk.
        var contract = await ContractAsync(StrictnessRule.PeriodTimeBudget);
        var receipt = new RedemptionReceipt([
            new RedemptionEntry(Guid.NewGuid(), WholeMinutes.Of(40)),
            new RedemptionEntry(Guid.NewGuid(), WholeMinutes.Of(20))]);
        var block = FocusBlock.CreateWithReceipt(Guid.NewGuid(), SeededFocusPresetId, At.Slot(Now.AddHours(1), Now.AddHours(2)),
            title: null, membership: TestContracts.Redemption(contract.Id), receipt: receipt);
        await SeedAsync(block);

        await ShouldBeAllowed(Command(block, Now.AddDays(1).AddHours(1), Now.AddDays(1).AddHours(2)));

        var stored = (FocusBlock)await Stored(block);
        stored.Scheduled.Should().Be(At.Slot(Now.AddDays(1).AddHours(1), Now.AddDays(1).AddHours(2)));
        stored.Receipt.Should().Be(receipt);
        stored.IsRedemption.Should().BeTrue();
    }

    // ── Floor protection ─────────────────────────────────────────────────────

    /// <summary>One committed two-hour Focus block on each of the contract's eight days (floor 2h); the one on day 0 is returned.</summary>
    private async Task<FocusBlock> TwoHoursEveryDayAsync(StrictnessRule rule)
    {
        var contract = await ContractAsync(rule, floorHours: 2);
        var blocks = Enumerable.Range(0, 8)
            .Select(day => Focus(Now.AddDays(day).AddHours(1), Now.AddDays(day).AddHours(3), TestContracts.Committed(contract.Id)))
            .ToArray<object>();
        await SeedAsync(blocks);
        return (FocusBlock)blocks[0];
    }

    [Fact]
    public async Task ShrinkingBelowTheDaysFloor_IsRefused_NamingTheDayAndTheFloor()
    {
        var block = await TwoHoursEveryDayAsync(StrictnessRule.DailyFlexibility);

        await ShouldBeRefused(block, Command(block, Now.AddHours(1), Now.AddHours(2)), // shrunk to 1h
            "This would drop Jan 1's total below its 2h daily floor.");
    }

    [Fact]
    public async Task MovingWithinTheDayKeepingItsLength_IsAllowed()
    {
        var block = await TwoHoursEveryDayAsync(StrictnessRule.DailyFlexibility);

        await ShouldBeAllowed(Command(block, Now.AddHours(4), Now.AddHours(6)));
    }

    [Fact]
    public async Task MovingToAnotherDay_LeavingItsOwnDayShort_IsRefused()
    {
        // The day left behind is checked although the block no longer touches it.
        var block = await TwoHoursEveryDayAsync(StrictnessRule.PeriodTimeBudget);

        await ShouldBeRefused(block, Command(block, Now.AddDays(2).AddHours(5), Now.AddDays(2).AddHours(7)),
            "This would drop Jan 1's total below its 2h daily floor.");
    }
}
