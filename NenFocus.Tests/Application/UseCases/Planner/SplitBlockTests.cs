using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NenFocus.Application.UseCases.Planner.SplitBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Application.UseCases.Planner;

/// <summary>
/// Splitting a block in two, run as the app runs it: real pipeline, real SQLite (Gap 110). Only a
/// Focus block a contract binds may be split, outside Lockdown (BlockChangeChecker.CheckSplit,
/// Gap 93); each piece must then pass placement against the calendar as it will be. The block
/// divides itself (FocusBlock.SplitInto): both pieces keep its membership and title, its receipt
/// is divided in time order, never recalculated, no piece shortened (Gap 106).
/// </summary>
public class SplitBlockTests : UseCaseTestBase
{
    private static readonly DateTime Now = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    // The original: 14:00–17:00, well after Now.
    private static readonly DateTime OriginalStart = new(2026, 1, 1, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OriginalEnd = new(2026, 1, 1, 17, 0, 0, DateTimeKind.Utc);
    // A later day, for blocks around midnight.
    private static readonly DateTime Day1 = new(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = Day1.AddDays(1);

    public SplitBlockTests() => Clock.SetNow(Now);

    private static SplitBlockCommand Command(Block block, DateTime firstFrom, DateTime firstTo, DateTime secondFrom, DateTime secondTo) => new()
    {
        OriginalBlockId = block.Id,
        First = At.Slot(firstFrom, firstTo),
        Second = At.Slot(secondFrom, secondTo)
    };

    /// <summary>14:00–16:00 and 16:00–17:00.</summary>
    private static SplitBlockCommand AtTwoHours(Block block) =>
        Command(block, OriginalStart, OriginalStart.AddHours(2), OriginalStart.AddHours(2), OriginalEnd);

    private Task<SplitBlockResult> SplitAsync(SplitBlockCommand command) => RunAsync(command);

    /// <summary>A contract from Now's day for 30 days, Active on seal, stored.</summary>
    private async Task<NenContract> ContractAsync(StrictnessRule rule, WorkHours? workHours = null)
    {
        var contract = TestContracts.Seal(
            id: Guid.NewGuid(),
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: rule,
            calendar: TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(30), new DailyFloorRule.None(), workHours: workHours),
            floorRule: new DailyFloorRule.None(),
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Now.Date), timeZone: At.UtcZone);
        await SeedAsync(contract);
        return contract;
    }

    /// <summary>A committed Focus block under a new contract of the given strictness, stored.</summary>
    private async Task<FocusBlock> CommittedAsync(StrictnessRule rule, DateTime from, DateTime to, WorkHours? workHours = null)
    {
        var contract = await ContractAsync(rule, workHours);
        var block = FocusBlock.Create(Guid.NewGuid(), SeededFocusPresetId, At.Slot(from, to), "My Block", TestContracts.Committed(contract.Id));
        await SeedAsync(block);
        return block;
    }

    private Task<FocusBlock> DailyFlexBlockAsync() =>
        CommittedAsync(StrictnessRule.DailyFlexibility, OriginalStart, OriginalEnd);

    private async Task<(FocusBlock First, FocusBlock Second)> SplitIntoPiecesAsync(SplitBlockCommand command)
    {
        var result = await SplitAsync(command);
        result.Success.Should().BeTrue(result.ErrorMessage);
        return ((FocusBlock)(await FindAsync<Block>(result.FirstBlockId!.Value))!,
                (FocusBlock)(await FindAsync<Block>(result.SecondBlockId!.Value))!);
    }

    private async Task ShouldBeAllowed(SplitBlockCommand command)
    {
        var result = await SplitAsync(command);

        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    /// <summary>Refused, and the original is still there, unsplit.</summary>
    private async Task<SplitBlockResult> ShouldBeRefused(SplitBlockCommand command, string? message = null)
    {
        var before = await ReadAsync(context => context.Set<Block>().CountAsync());

        var result = await SplitAsync(command);

        result.Success.Should().BeFalse();
        if (message is not null) result.ErrorMessage.Should().Be(message);
        (await ReadAsync(context => context.Set<Block>().CountAsync())).Should().Be(before);
        return result;
    }

    // ── What gets stored ─────────────────────────────────────────────────────

    [Fact]
    public async Task TheOriginalIsReplacedByTwoPieces_KeepingItsPresetMembershipAndTitle()
    {
        var block = await DailyFlexBlockAsync();

        var (first, second) = await SplitIntoPiecesAsync(AtTwoHours(block));

        (await FindAsync<Block>(block.Id)).Should().BeNull();
        new[] { first.Id, second.Id }.Should().OnlyHaveUniqueItems().And.NotContain(block.Id);
        first.Scheduled.Should().Be(At.Slot(OriginalStart, OriginalStart.AddHours(2)));
        second.Scheduled.Should().Be(At.Slot(OriginalStart.AddHours(2), OriginalEnd));
        foreach (var piece in new[] { first, second })
        {
            piece.BlockPresetId.Should().Be(SeededFocusPresetId);
            piece.Membership.Should().Be(block.Membership);
            piece.Title.Should().Be("My Block");
        }
    }

    [Fact]
    public async Task ARedemptionBlock_DividesItsReceipt_AndShortensNeitherPiece()
    {
        // A 3h block covering 90 min, cut at 2h: the first piece takes all 90 minutes, the second
        // none — and both keep the requested slots (Gap 95, decision 16).
        var contract = await ContractAsync(StrictnessRule.DailyFlexibility);
        var missedBlockId = Guid.NewGuid();
        var block = FocusBlock.CreateWithReceipt(Guid.NewGuid(), SeededFocusPresetId, At.Slot(OriginalStart, OriginalEnd), "My Block",
            TestContracts.Redemption(contract.Id), receipt: new RedemptionReceipt([new(missedBlockId, WholeMinutes.Of(90))]));
        await SeedAsync(block);

        var (first, second) = await SplitIntoPiecesAsync(AtTwoHours(block));

        first.Receipt.Entries.Should().Equal(new RedemptionEntry(missedBlockId, WholeMinutes.Of(90)));
        second.Receipt.Should().Be(RedemptionReceipt.None);
        first.Scheduled.Should().Be(At.Slot(OriginalStart, OriginalStart.AddHours(2)));
        second.Scheduled.Should().Be(At.Slot(OriginalStart.AddHours(2), OriginalEnd));
        first.IsRedemption.Should().BeTrue();
        second.IsRedemption.Should().BeTrue();
    }

    // ── Form and the shape of a split ────────────────────────────────────────

    [Fact]
    public async Task AnEmptyBlockId_IsRefused()
    {
        var block = await DailyFlexBlockAsync();

        await ShouldBeRefused(AtTwoHours(block) with { OriginalBlockId = Guid.Empty });
    }

    [Fact]
    public async Task AnUnknownBlock_IsRefused()
    {
        var notStored = FocusBlock.Create(Guid.NewGuid(), SeededFocusPresetId, At.Slot(OriginalStart, OriginalEnd));

        await ShouldBeRefused(AtTwoHours(notStored), "The block to split could not be found.");
    }

    [Fact]
    public async Task PiecesThatOverlapEachOther_AreRefused()
    {
        var block = await DailyFlexBlockAsync();

        // 14:00–16:00 and 15:00–17:00.
        await ShouldBeRefused(Command(block, OriginalStart, OriginalStart.AddHours(2), OriginalStart.AddHours(1), OriginalEnd),
            "The two resulting blocks cannot overlap each other.");
    }

    [Fact]
    public async Task PiecesNotAddingUpToTheOriginal_AreRefused()
    {
        var block = await DailyFlexBlockAsync();

        // 2h + 30 min of a 3h block.
        await ShouldBeRefused(Command(block, OriginalStart, OriginalStart.AddHours(2), OriginalStart.AddHours(2), OriginalStart.AddHours(2).AddMinutes(30)),
            "The two resulting blocks must add up to the original block's total duration.");
    }

    [Fact]
    public async Task APieceMovedToAnotherDay_IsRefused()
    {
        var block = await DailyFlexBlockAsync();

        await ShouldBeRefused(Command(block, OriginalStart, OriginalStart.AddHours(2), OriginalStart.AddHours(2).AddDays(1), OriginalEnd.AddDays(1)));
    }

    [Fact]
    public async Task APieceThatItselfCrossesMidnight_IsRefused()
    {
        var block = await CommittedAsync(StrictnessRule.DailyFlexibility, Day1.AddHours(22), Day2.AddHours(2)); // 4h

        await ShouldBeRefused(Command(block, Day1.AddHours(22), Day2.AddHours(1), Day2.AddHours(1), Day2.AddHours(2)),
            "The first block must land entirely on one day.");
    }

    [Fact]
    public async Task APieceEndingExactlyAtMidnight_StaysOnItsDay()
    {
        var block = await CommittedAsync(StrictnessRule.DailyFlexibility, Day1.AddHours(22), Day1.AddHours(24));

        await ShouldBeAllowed(Command(block, Day1.AddHours(22), Day1.AddHours(23), Day1.AddHours(23), Day1.AddHours(24)));
    }

    [Fact]
    public async Task ACrossMidnightOriginal_MayLandAPieceOnEitherDay()
    {
        var block = await CommittedAsync(StrictnessRule.DailyFlexibility, Day1.AddHours(23), Day2.AddHours(1));

        // 30 min entirely on Day1, 90 min entirely on Day2.
        await ShouldBeAllowed(Command(block, Day1.AddHours(23), Day1.AddHours(23).AddMinutes(30), Day2, Day2.AddHours(1).AddMinutes(30)));
    }

    [Fact]
    public async Task ACrossMidnightOriginal_MayLandBothPiecesOnOneDay_WhenNoFloorSaysOtherwise()
    {
        // No floor here, to isolate day membership from the floor (see the floor tests below).
        var block = await CommittedAsync(StrictnessRule.DailyFlexibility, Day1.AddHours(23), Day2.AddHours(1));

        await ShouldBeAllowed(Command(block, Day1.AddHours(10), Day1.AddHours(11), Day1.AddHours(11), Day1.AddHours(12)));
    }

    // ── Permission: BlockChangeChecker, asked before placement ──────────────

    [Fact]
    public async Task APlainBlock_IsRefused()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), SeededFocusPresetId, At.Slot(OriginalStart, OriginalEnd));
        await SeedAsync(block);

        await ShouldBeRefused(AtTwoHours(block));
    }

    [Fact]
    public async Task UnderLockdown_IsRefused_BeforePlacementIsAsked()
    {
        var block = await CommittedAsync(StrictnessRule.Locked, OriginalStart, OriginalEnd);

        // The first piece is also in the past, so placement would refuse too — permission answers first.
        await ShouldBeRefused(Command(block, OriginalStart.AddHours(-8), OriginalStart.AddHours(-6), OriginalStart.AddHours(2), OriginalEnd),
            "This block is locked by a Lockdown contract.");
    }

    [Fact]
    public async Task UnderPeriodTimeBudget_IsAllowed()
    {
        // Only Lockdown is immutable.
        var block = await CommittedAsync(StrictnessRule.PeriodTimeBudget, OriginalStart, OriginalEnd);

        await ShouldBeAllowed(AtTwoHours(block));
    }

    [Fact]
    public async Task ACancelledBlock_IsRefused()
    {
        var contract = await ContractAsync(StrictnessRule.DailyFlexibility);
        var block = FocusBlock.Create(Guid.NewGuid(), SeededFocusPresetId, At.Slot(OriginalStart, OriginalEnd), membership: TestContracts.Committed(contract.Id));
        block.MarkCancelled(At.Utc(Now));
        await SeedAsync(block);

        await ShouldBeRefused(AtTwoHours(block));
    }

    [Fact]
    public async Task AnEndedBlock_IsRefused()
    {
        var block = await CommittedAsync(StrictnessRule.DailyFlexibility, Now.AddHours(-3), Now.AddHours(-1));

        await ShouldBeRefused(Command(block, Now.AddHours(-3), Now.AddHours(-2), Now.AddHours(-2), Now.AddHours(-1)));
    }

    [Fact]
    public async Task ABackgroundBlock_IsRefused()
    {
        // Split is Focus-only, however valid the rest of the command.
        var contract = await ContractAsync(StrictnessRule.DailyFlexibility);
        var block = BackgroundBlock.Create(Guid.NewGuid(), SeededBackgroundPresetId, At.Slot(OriginalStart, OriginalEnd), membership: TestContracts.Committed(contract.Id));
        await SeedAsync(block);

        await ShouldBeRefused(AtTwoHours(block));
    }

    [Fact]
    public async Task AFreeBonusBlock_IsRefused()
    {
        var contract = await ContractAsync(StrictnessRule.DailyFlexibility);
        var block = FocusBlock.Create(Guid.NewGuid(), SeededFocusPresetId, At.Slot(OriginalStart, OriginalEnd), membership: contract.MembershipForPlacedBlock());
        await SeedAsync(block);

        await ShouldBeRefused(AtTwoHours(block), "Only a Focus block that a Nen Contract binds can be split.");
    }

    // ── Floor protection: the real checker over the contract's real blocks ──

    /// <summary>
    /// A committed block across midnight, 23:00 on day 1 to 01:00 on day 2 — one hour on each day —
    /// in a two-day contract with <paramref name="floorHours"/> a day. With a floor above one hour,
    /// a second committed hour sits on each day so the plan meets it before the split.
    /// </summary>
    private async Task<FocusBlock> CrossMidnightBlockUnderAFloorAsync(StrictnessRule rule, double floorHours)
    {
        var (day1, day2) = (DateOnly.FromDateTime(Day1), DateOnly.FromDateTime(Day2));
        var contract = NenContract.Seal(Guid.NewGuid(), "Contract", new NenContractGoal("Goal"),
            TestRules.Of(rule, new DailyFloor(floorHours)),
            TestCalendars.For(day1, day2, new DailyFloorRule.Fixed(new DailyFloor(floorHours))),
            new Dictionary<DateOnly, double> { [day1] = floorHours, [day2] = floorHours },
            At.Utc(Now), At.UtcZone);
        FocusBlock Committed(DateTime from, DateTime to) =>
            FocusBlock.Create(Guid.NewGuid(), SeededFocusPresetId, At.Slot(from, to), membership: TestContracts.Committed(contract.Id));

        var block = Committed(Day1.AddHours(23), Day2.AddHours(1));
        var members = new List<object> { contract, block };
        if (floorHours > 1)
        {
            members.Add(Committed(Day1.AddHours(8), Day1.AddHours(9)));
            members.Add(Committed(Day2.AddHours(8), Day2.AddHours(9)));
        }
        await SeedAsync(members.ToArray());
        return block;
    }

    [Theory]
    [InlineData(StrictnessRule.DailyFlexibility)]
    [InlineData(StrictnessRule.PeriodTimeBudget)]
    public async Task BothPiecesOnOneDay_LeavingTheOtherBelowItsFloor_IsRefused(StrictnessRule rule)
    {
        // Day 2 keeps only its other committed hour — below its 2h floor. A day's floor is a day's
        // floor, whichever the strictness.
        var block = await CrossMidnightBlockUnderAFloorAsync(rule, floorHours: 2);

        await ShouldBeRefused(Command(block, Day1.AddHours(10), Day1.AddHours(11), Day1.AddHours(11), Day1.AddHours(12)),
            "This would drop Jan 4's total below its 2h daily floor.");
    }

    [Fact]
    public async Task OnePieceOnEachDay_MeetingBothFloors_IsAllowed()
    {
        var block = await CrossMidnightBlockUnderAFloorAsync(StrictnessRule.DailyFlexibility, floorHours: 1);

        await ShouldBeAllowed(Command(block, Day1.AddHours(23), Day2, Day2, Day2.AddHours(1)));
    }

    [Theory]
    [InlineData(10, false)] // the contract's only block is 3h — under a 10h target whatever the split
    [InlineData(3, true)]
    public async Task UnderPeriodTimeBudgetWithAWholePeriodTarget_TheTotalIsChecked(double targetHours, bool allowed)
    {
        var day1 = DateOnly.FromDateTime(OriginalStart);
        var contract = NenContract.Seal(Guid.NewGuid(), "Contract", new NenContractGoal("Goal"),
            TestRules.Of(StrictnessRule.PeriodTimeBudget),
            TestCalendars.For(day1, day1.AddDays(30), new DailyFloorRule.None()),
            new Dictionary<DateOnly, double> { [day1] = targetHours }, // the target is what was signed
            At.Utc(day1.ToDateTime(TimeOnly.MinValue)), At.UtcZone);
        var block = FocusBlock.Create(Guid.NewGuid(), SeededFocusPresetId, At.Slot(OriginalStart, OriginalEnd), membership: TestContracts.Committed(contract.Id));
        await SeedAsync(contract, block);

        var result = await SplitAsync(AtTwoHours(block));

        result.Success.Should().Be(allowed, result.ErrorMessage);
    }

    // ── Placement of each piece ──────────────────────────────────────────────

    [Fact]
    public async Task APieceOverAnotherFocusBlock_IsRefused()
    {
        var block = await DailyFlexBlockAsync();
        await SeedAsync(FocusBlock.Create(Guid.NewGuid(), SeededFocusPresetId, At.Slot(OriginalStart.AddHours(-2), OriginalStart.AddHours(-1))));

        // 12:00–14:00 + 14:00–15:00: the first piece lands on the 12:00–13:00 block.
        await ShouldBeRefused(Command(block, OriginalStart.AddHours(-2), OriginalStart, OriginalStart, OriginalStart.AddHours(1)),
            "First block: A Focus block already exists in this time window.");
    }

    [Fact]
    public async Task APieceOverTheRunningSession_IsRefused()
    {
        // Gap 88: Split respects a running session too (BlockPlacementChecker.CheckFree).
        var block = await DailyFlexBlockAsync();
        var running = Session.Start(Guid.NewGuid(), new SessionGoal("Focus"),
            FocusBlock.Spontaneous(Guid.NewGuid(), SeededFocusPresetId, At.Slot(OriginalStart.AddMinutes(30), OriginalStart.AddMinutes(90))),
            At.Utc(OriginalStart.AddMinutes(30)));
        await SeedAsync(running);

        await ShouldBeRefused(AtTwoHours(block), "First block: A Focus block already exists in this time window.");
    }

    [Fact]
    public async Task APieceInThePast_IsRefused()
    {
        // Gap 88 phase 8: the original hasn't started, but a piece may be moved to an hour of the
        // same day that has passed (Now is 08:00).
        var block = await DailyFlexBlockAsync();

        await ShouldBeRefused(Command(block, OriginalStart.AddHours(-8), OriginalStart.AddHours(-7), OriginalStart.AddHours(1), OriginalEnd),
            "First block: Start time must be in the future.");
    }

    [Fact]
    public async Task TheFirstPieceOutsideWorkHours_IsRefused()
    {
        // Work hours 15:00–18:00; the first piece starts at 14:00.
        var block = await CommittedAsync(StrictnessRule.DailyFlexibility, OriginalStart, OriginalEnd, new WorkHours(new TimeOnly(15, 0), new TimeOnly(18, 0)));

        await ShouldBeRefused(AtTwoHours(block));
    }

    [Fact]
    public async Task TheSecondPieceOutsideWorkHours_IsRefused()
    {
        // Work hours 09:00–16:00; the second piece ends at 17:00.
        var block = await CommittedAsync(StrictnessRule.DailyFlexibility, OriginalStart, OriginalEnd, new WorkHours(new TimeOnly(9, 0), new TimeOnly(16, 0)));

        await ShouldBeRefused(AtTwoHours(block));
    }

    [Fact]
    public async Task BothPiecesWithinWorkHours_IsAllowed()
    {
        var block = await CommittedAsync(StrictnessRule.DailyFlexibility, OriginalStart, OriginalEnd, new WorkHours(new TimeOnly(9, 0), new TimeOnly(18, 0)));

        await ShouldBeAllowed(AtTwoHours(block));
    }
}
