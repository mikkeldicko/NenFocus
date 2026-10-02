using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

// Placement is the validator's job via GetBlockPlacementVerdictQuery since Gap 88 phase 8 —
// tested in GetBlockPlacementVerdictQueryHandlerTests. The cap is attribution's (its own
// tests). The handler only saves what attribution returns.
public class UpdatePlannedBlockCommandHandlerTests
{
    private static readonly DateTime Start = new(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 2, 11, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> _computeRedemptionAttribution =
        Substitute.For<IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult>>();
    private readonly ISystemClock _clock = TestClock.Create();

    public UpdatePlannedBlockCommandHandlerTests()
    {
        _clock.SetNow(Start.AddDays(-3));
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[]);

        // Default: no entries, the requested slot unchanged.
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var query = (ComputeRedemptionAttributionQuery)ci[0];
                return new RedemptionAttributionResult([], query.Block);
            });
    }

    private UpdatePlannedBlockCommandHandler CreateHandler() => new(_blocks, _presets, _contracts, _computeRedemptionAttribution, new BlockPlacementChecker(), _clock);

    private static UpdatePlannedBlockCommand ValidCommand(Guid blockId, Guid presetId) => new()
    {
        BlockId = blockId,
        PresetId = presetId,
        Slot = At.Slot(Start, End)
    };

    private static BlockPreset FocusPreset(Guid id)
        => BlockPreset.Create(id, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Start.AddDays(-2)));

    [Fact]
    public async Task HandleAsync_BlockNotFound_Fails()
    {
        _blocks.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PlannedBlock?)null);

        var result = await CreateHandler().HandleAsync(ValidCommand(Guid.NewGuid(), Guid.NewGuid()));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_PresetNotFound_Fails()
    {
        var blockId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(BackgroundBlock.Create(blockId, Guid.NewGuid(),At.Slot(Start, End)));
        _presets.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId, Guid.NewGuid()));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_BackgroundBlock_Reschedules()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var block = BackgroundBlock.Create(blockId, presetId, At.Slot(Start.AddDays(-1), End.AddDays(-1)));
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Background", WebsiteFilterType.Blocklist, BlockPresetType.Background, At.Utc(Start.AddDays(-2))));

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId, presetId));

        result.Success.Should().BeTrue();
        block.Scheduled.Should().Be(At.Slot(Start, End));
        await _computeRedemptionAttribution.DidNotReceive().HandleAsync(
            Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_HappyPath_ReschedulesAndReassignsPreset()
    {
        var blockId = Guid.NewGuid();
        var oldPresetId = Guid.NewGuid();
        var newPresetId = Guid.NewGuid();
        var block = FocusBlock.Create(blockId, oldPresetId,At.Slot(Start.AddDays(-1), End.AddDays(-1)));
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(newPresetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(newPresetId));

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId, newPresetId));

        result.Success.Should().BeTrue();
        block.Scheduled.Should().Be(At.Slot(Start, End));
        block.BlockPresetId.Should().Be(newPresetId);
        await _blocks.Received(1).UpdateAsync(block, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RedemptionBlock_RecomputesItsEntriesFromAttribution()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(blockId, presetId,At.Slot(Start, End), membership: TestContracts.Redemption(contractId),
            redemptionEntries: [new RedemptionEntry(Guid.NewGuid(), 20)]);
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));
        var newMissedBlockId = Guid.NewGuid();
        _computeRedemptionAttribution
            .HandleAsync(Arg.Is<ComputeRedemptionAttributionQuery>(q => q.ContractId == contractId && q.ExcludeBlockId == blockId),
                Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult([new(newMissedBlockId, 60)], At.Slot(Start, End)));

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId, presetId));

        result.Success.Should().BeTrue();
        block.RedemptionEntries.Should().ContainSingle(e => e.MissedBlockId == newMissedBlockId && e.MinutesCovered == 60);
    }

    [Fact]
    public async Task HandleAsync_RedemptionBlock_NoLongerEligible_ClearsItsEntriesButStaysARedemptionBlock()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(blockId, presetId,At.Slot(Start, End), membership: TestContracts.Redemption(contractId),
            redemptionEntries: [new RedemptionEntry(Guid.NewGuid(), 20)]);
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));

        await CreateHandler().HandleAsync(ValidCommand(blockId, presetId));

        block.RedemptionEntries.Should().BeEmpty();
        block.IsRedemption.Should().BeTrue("a role is set once and never changes");
    }

    [Fact]
    public async Task HandleAsync_FocusBlock_NoContractId_NeverConsultsRedemptionAttribution()
    {
        // A standalone block (no contract) never auto-discovers one via editing — only a
        // block already tied to a contract gets its coverage recomputed.
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var block = FocusBlock.Create(blockId, presetId,At.Slot(Start, End));
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));

        await CreateHandler().HandleAsync(ValidCommand(blockId, presetId));

        block.RedemptionEntries.Should().BeEmpty();
        await _computeRedemptionAttribution.DidNotReceive().HandleAsync(
            Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>());
    }

    // ── Membership on a move (Gap 97) ────────────────────────────────────────

    /// <summary>A contract in effect covering the whole of 2–9 January — the command's slot is inside it.</summary>
    private Guid SetUpContractAroundTheSlot()
    {
        var contract = NenContract.Seal(
            id: Guid.NewGuid(),
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.PeriodTimeBudget,
            calendar: TestCalendars.For(DateOnly.FromDateTime(Start), DateOnly.FromDateTime(Start).AddDays(7), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Start.Date), timeZone: At.UtcZone);
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);
        return contract.Id;
    }

    private FocusBlock SetUpBlock(TimeSlot scheduled, ContractMembership? membership, Guid presetId)
    {
        var block = FocusBlock.Create(Guid.NewGuid(), presetId, scheduled, membership: membership);
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));
        return block;
    }

    [Fact]
    public async Task HandleAsync_PlainBlockMovedIntoAContract_BecomesItsBonusBlock_AndClaimsNothing()
    {
        var contractId = SetUpContractAroundTheSlot();
        var presetId = Guid.NewGuid();
        var block = SetUpBlock(At.Slot(Start.AddDays(-1), End.AddDays(-1)), membership: null, presetId); // the day before the contract

        await CreateHandler().HandleAsync(ValidCommand(block.Id, presetId));

        block.Membership.Should().Be(TestContracts.Bonus(contractId));
        block.RedemptionEntries.Should().BeEmpty();
        await _computeRedemptionAttribution.DidNotReceive().HandleAsync(
            Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BonusBlockMovedOutOfItsContract_BecomesAPlainBlock()
    {
        var contractId = SetUpContractAroundTheSlot();
        var presetId = Guid.NewGuid();
        var block = SetUpBlock(At.Slot(Start, End), TestContracts.Bonus(contractId), presetId);
        var afterTheContract = At.Slot(Start.AddDays(20), End.AddDays(20));

        await CreateHandler().HandleAsync(ValidCommand(block.Id, presetId) with { Slot = afterTheContract });

        block.Membership.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_CommittedBlockEdited_KeepsItsMembership_AndNeverClaimsADeficit()
    {
        // Before Gap 97 any block with a contract id had its attribution recomputed, new
        // claims included — so an edit could turn committed hours into redemption coverage.
        var contractId = SetUpContractAroundTheSlot();
        var presetId = Guid.NewGuid();
        var block = SetUpBlock(At.Slot(Start.AddHours(3), End.AddHours(3)), TestContracts.Committed(contractId), presetId);
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult([new(Guid.NewGuid(), 60)], At.Slot(Start, End)));

        await CreateHandler().HandleAsync(ValidCommand(block.Id, presetId));

        block.Membership.Should().Be(TestContracts.Committed(contractId));
        block.RedemptionEntries.Should().BeEmpty();
        await _computeRedemptionAttribution.DidNotReceive().HandleAsync(
            Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_FocusBlock_AttributionReturnsACappedSlot_SavesItAndReportsCapped()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(blockId, presetId,At.Slot(Start, End), membership: TestContracts.Redemption(contractId),
            redemptionEntries: [new RedemptionEntry(Guid.NewGuid(), 60)]);
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult([new(Guid.NewGuid(), 45)], At.Slot(Start, Start.AddMinutes(45)), true));

        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Start, Start.AddHours(2)) };
        var result = await CreateHandler().HandleAsync(command);

        result.Success.Should().BeTrue();
        result.RedemptionCapped.Should().BeTrue();
        block.Scheduled.Should().Be(At.Slot(Start, Start.AddMinutes(45)));
    }

    [Fact]
    public async Task HandleAsync_FocusBlock_AttributionReturnsTheRequestedSlot_DoesNotReportCapped()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(blockId, presetId,At.Slot(Start, Start.AddHours(2)), membership: TestContracts.Redemption(contractId),
            redemptionEntries: [new RedemptionEntry(Guid.NewGuid(), 120)]);
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult([new(Guid.NewGuid(), 30)], At.Slot(Start, Start.AddMinutes(30))));

        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Start, Start.AddMinutes(30)) };
        var result = await CreateHandler().HandleAsync(command);

        result.RedemptionCapped.Should().BeFalse();
        block.Scheduled.Should().Be(At.Slot(Start, Start.AddMinutes(30)));
    }
}
