using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

// Placement is the validator's job via GetBlockPlacementVerdictQuery since Gap 88 phase 8 —
// tested in GetBlockPlacementVerdictQueryHandlerTests. The handler saves the requested slot; a
// redemption block keeps its receipt (Gap 106, decision 3).
public class UpdatePlannedBlockCommandHandlerTests
{
    private static readonly DateTime Start = new(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 2, 11, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();

    private UpdatePlannedBlockCommandHandler CreateHandler() => new(_blocks, _presets);

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
    public async Task HandleAsync_RedemptionBlockMoved_KeepsItsReceipt()
    {
        // Gap 106, decision 3: a redemption block's claims are set once, when it is placed; a move
        // keeps them (and its length — Gap 95, decision 16), even when the debt has since shrunk.
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var receipt = new RedemptionReceipt([new RedemptionEntry(Guid.NewGuid(), WholeMinutes.Of(40)), new RedemptionEntry(Guid.NewGuid(), WholeMinutes.Of(20))]);
        var block = FocusBlock.CreateWithReceipt(Guid.NewGuid(), presetId, At.Slot(Start, End),
            title: null, membership: TestContracts.Redemption(contractId), receipt: receipt);
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));
        var later = At.Slot(Start.AddDays(1), End.AddDays(1));

        var result = await CreateHandler().HandleAsync(ValidCommand(block.Id, presetId) with { Slot = later });

        result.Success.Should().BeTrue();
        block.Scheduled.Should().Be(later);
        block.Receipt.Should().Be(receipt);
        block.IsRedemption.Should().BeTrue();
    }

    // ── Membership on a move (Gap 97) ────────────────────────────────────────

    private FocusBlock SetUpBlock(TimeSlot scheduled, ContractMembership? membership, Guid presetId)
    {
        var block = FocusBlock.Create(Guid.NewGuid(), presetId, scheduled, membership: membership);
        _blocks.GetByIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));
        return block;
    }

    [Fact]
    public async Task HandleAsync_PlainBlock_StaysPlain_AMoveNeverChangesMembership()
    {
        // Gap 106, decision 18: a plain block moved where a contract governs is refused by the
        // validator (BlockChangeChecker); the handler never makes it a member.
        var presetId = Guid.NewGuid();
        var block = SetUpBlock(At.Slot(Start.AddDays(-1), End.AddDays(-1)), membership: null, presetId);

        await CreateHandler().HandleAsync(ValidCommand(block.Id, presetId));

        block.Membership.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ABonusBlock_StaysItsContractsBlock_WhereverTheSlotIs()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var block = SetUpBlock(At.Slot(Start, End), TestContracts.Bonus(contractId), presetId);
        var afterTheContract = At.Slot(Start.AddDays(20), End.AddDays(20));

        // The validator refuses a move out of the contract's period (Gap 93); the handler never
        // ends a membership as a side effect of a move.
        await CreateHandler().HandleAsync(ValidCommand(block.Id, presetId) with { Slot = afterTheContract });

        block.Membership.Should().Be(TestContracts.Bonus(contractId));
    }

    [Fact]
    public async Task HandleAsync_CommittedBlockEdited_KeepsItsMembership_AndNeverClaimsADeficit()
    {
        // Before Gap 97 any block with a contract id had its attribution recomputed, new
        // claims included — so an edit could turn committed hours into redemption coverage.
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var block = SetUpBlock(At.Slot(Start.AddHours(3), End.AddHours(3)), TestContracts.Committed(contractId), presetId);

        await CreateHandler().HandleAsync(ValidCommand(block.Id, presetId));

        block.Membership.Should().Be(TestContracts.Committed(contractId));
        block.Receipt.Entries.Should().BeEmpty();
    }
}
