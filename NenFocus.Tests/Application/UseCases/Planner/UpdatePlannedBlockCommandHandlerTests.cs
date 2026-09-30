using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
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
    private readonly IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> _computeRedemptionAttribution =
        Substitute.For<IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult>>();

    public UpdatePlannedBlockCommandHandlerTests()
    {
        // Default: no entries, the requested slot unchanged.
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var query = (ComputeRedemptionAttributionQuery)ci[0];
                return new RedemptionAttributionResult(query.ContractId, [], query.Block);
            });
    }

    private UpdatePlannedBlockCommandHandler CreateHandler() => new(_blocks, _presets, _computeRedemptionAttribution);

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
    public async Task HandleAsync_FocusBlock_WithContractId_RecomputesRedemptionEntriesFromAttribution()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(blockId, presetId,At.Slot(Start, End), contractId: contractId,
            redemptionEntries: [new RedemptionEntry(Guid.NewGuid(), 20)]);
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));
        var newMissedBlockId = Guid.NewGuid();
        _computeRedemptionAttribution
            .HandleAsync(Arg.Is<ComputeRedemptionAttributionQuery>(q => q.ContractId == contractId && q.ExcludeBlockId == blockId),
                Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult(contractId, [new(newMissedBlockId, 60)], At.Slot(Start, End)));

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId, presetId));

        result.Success.Should().BeTrue();
        block.RedemptionEntries.Should().ContainSingle(e => e.MissedBlockId == newMissedBlockId && e.MinutesCovered == 60);
    }

    [Fact]
    public async Task HandleAsync_FocusBlock_WithContractId_NoLongerEligible_ClearsExistingEntries()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(blockId, presetId,At.Slot(Start, End), contractId: contractId,
            redemptionEntries: [new RedemptionEntry(Guid.NewGuid(), 20)]);
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));

        await CreateHandler().HandleAsync(ValidCommand(blockId, presetId));

        block.RedemptionEntries.Should().BeEmpty();
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

    [Fact]
    public async Task HandleAsync_FocusBlock_AttributionReturnsACappedSlot_SavesItAndReportsCapped()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(blockId, presetId,At.Slot(Start, End), contractId: contractId,
            redemptionEntries: [new RedemptionEntry(Guid.NewGuid(), 60)]);
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult(contractId, [new(Guid.NewGuid(), 45)], At.Slot(Start, Start.AddMinutes(45)), true));

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
        var block = FocusBlock.Create(blockId, presetId,At.Slot(Start, Start.AddHours(2)), contractId: contractId,
            redemptionEntries: [new RedemptionEntry(Guid.NewGuid(), 120)]);
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult(contractId, [new(Guid.NewGuid(), 30)], At.Slot(Start, Start.AddMinutes(30))));

        var command = ValidCommand(blockId, presetId) with { Slot = At.Slot(Start, Start.AddMinutes(30)) };
        var result = await CreateHandler().HandleAsync(command);

        result.RedemptionCapped.Should().BeFalse();
        block.Scheduled.Should().Be(At.Slot(Start, Start.AddMinutes(30)));
    }
}
