using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

// Placement ("is this time free", "in the past", "inside the contract's hours") is the
// validator's job via GetBlockPlacementVerdictQuery since Gap 88 phase 8 — tested in
// GetBlockPlacementVerdictQueryHandlerTests. The cap is attribution's (its own tests).
// The handler only saves what attribution returns.
public class CreatePlannedBlockCommandHandlerTests
{
    private static readonly DateTime Start = new(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 2, 11, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> _computeRedemptionAttribution =
        Substitute.For<IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult>>();

    public CreatePlannedBlockCommandHandlerTests()
    {
        // Default: echoes back whatever ContractId was passed in, no redemption entries, the
        // requested slot unchanged — the real query's answer when nothing is eligible to cover.
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var query = (ComputeRedemptionAttributionQuery)ci[0];
                return new RedemptionAttributionResult(query.ContractId, [], query.Block);
            });
    }

    private CreatePlannedBlockCommandHandler CreateHandler() => new(_blocks, _presets, _computeRedemptionAttribution);

    private static CreatePlannedBlockCommand FocusCommand(Guid presetId) => new()
    {
        PresetId = presetId,
        BlockPresetType = BlockPresetType.Focus,
        Slot = At.Slot(Start, End)
    };

    private void SetUpPreset(Guid presetId, BlockPresetType type) =>
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Preset", WebsiteFilterType.Blocklist, type, At.Utc(Start.AddDays(-1))));

    [Fact]
    public async Task HandleAsync_PresetNotFound_Fails()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);

        var result = await CreateHandler().HandleAsync(FocusCommand(presetId));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_Focus_HappyPath_CreatesFocusBlock()
    {
        var presetId = Guid.NewGuid();
        SetUpPreset(presetId, BlockPresetType.Focus);

        var result = await CreateHandler().HandleAsync(FocusCommand(presetId));

        result.Success.Should().BeTrue();
        result.BlockId.Should().NotBeNull();
        await _blocks.Received(1).AddAsync(Arg.Is<PlannedBlock>(b => b is FocusBlock), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Background_CreatesBackgroundBlock()
    {
        var presetId = Guid.NewGuid();
        SetUpPreset(presetId, BlockPresetType.Background);
        var command = FocusCommand(presetId) with { BlockPresetType = BlockPresetType.Background };

        var result = await CreateHandler().HandleAsync(command);

        result.Success.Should().BeTrue();
        await _blocks.Received(1).AddAsync(Arg.Is<PlannedBlock>(b => b is BackgroundBlock), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithContractId_LinksTheCreatedBlock()
    {
        var presetId = Guid.NewGuid();
        SetUpPreset(presetId, BlockPresetType.Focus);
        var contractId = Guid.NewGuid();

        PlannedBlock? saved = null;
        await _blocks.AddAsync(Arg.Do<PlannedBlock>(b => saved = b), Arg.Any<CancellationToken>());

        await CreateHandler().HandleAsync(FocusCommand(presetId) with { ContractId = contractId });

        saved!.ContractId.Should().Be(contractId);
    }

    [Fact]
    public async Task HandleAsync_Focus_AttributionFindsADeficit_SetsRedemptionEntriesAndContractId()
    {
        var presetId = Guid.NewGuid();
        SetUpPreset(presetId, BlockPresetType.Focus);
        var missedBlockId = Guid.NewGuid();
        var discoveredContractId = Guid.NewGuid();
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult(discoveredContractId, [new(missedBlockId, 60)], At.Slot(Start, End)));

        PlannedBlock? saved = null;
        await _blocks.AddAsync(Arg.Do<PlannedBlock>(b => saved = b), Arg.Any<CancellationToken>());

        await CreateHandler().HandleAsync(FocusCommand(presetId));

        var focusBlock = saved.Should().BeOfType<FocusBlock>().Subject;
        focusBlock.ContractId.Should().Be(discoveredContractId);
        focusBlock.RedemptionEntries.Should().ContainSingle(e => e.MissedBlockId == missedBlockId && e.MinutesCovered == 60);
    }

    [Fact]
    public async Task HandleAsync_Background_NeverConsultsRedemptionAttribution()
    {
        var presetId = Guid.NewGuid();
        SetUpPreset(presetId, BlockPresetType.Background);
        var command = FocusCommand(presetId) with { BlockPresetType = BlockPresetType.Background };

        await CreateHandler().HandleAsync(command);

        await _computeRedemptionAttribution.DidNotReceive().HandleAsync(
            Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Focus_DeclineRedemptionTrue_SkipsAttributionAndCreatesUnlinkedBlock()
    {
        var presetId = Guid.NewGuid();
        SetUpPreset(presetId, BlockPresetType.Focus);
        // A real match exists — proves DeclineRedemption skips it rather than the match
        // simply not being found.
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult(Guid.NewGuid(), [new(Guid.NewGuid(), 45)], At.Slot(Start, Start.AddMinutes(45)), true));

        PlannedBlock? saved = null;
        await _blocks.AddAsync(Arg.Do<PlannedBlock>(b => saved = b), Arg.Any<CancellationToken>());

        var result = await CreateHandler().HandleAsync(FocusCommand(presetId) with { DeclineRedemption = true });

        result.Success.Should().BeTrue();
        await _computeRedemptionAttribution.DidNotReceive().HandleAsync(
            Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>());
        var focusBlock = saved.Should().BeOfType<FocusBlock>().Subject;
        focusBlock.ContractId.Should().BeNull();
        focusBlock.RedemptionEntries.Should().BeEmpty();
        focusBlock.Scheduled.Should().Be(At.Slot(Start, End));
    }

    [Fact]
    public async Task HandleAsync_Focus_AttributionReturnsACappedSlot_SavesItAndReportsCapped()
    {
        var presetId = Guid.NewGuid();
        SetUpPreset(presetId, BlockPresetType.Focus);
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult(Guid.NewGuid(), [new(Guid.NewGuid(), 45)], At.Slot(Start, Start.AddMinutes(45)), true));

        PlannedBlock? saved = null;
        await _blocks.AddAsync(Arg.Do<PlannedBlock>(b => saved = b), Arg.Any<CancellationToken>());

        var command = FocusCommand(presetId) with { Slot = At.Slot(Start, Start.AddHours(2)) };
        var result = await CreateHandler().HandleAsync(command);

        result.Success.Should().BeTrue();
        result.RedemptionCapped.Should().BeTrue();
        saved!.Scheduled.Should().Be(At.Slot(Start, Start.AddMinutes(45)));
    }

    [Fact]
    public async Task HandleAsync_Focus_AttributionReturnsTheRequestedSlot_DoesNotReportCapped()
    {
        var presetId = Guid.NewGuid();
        SetUpPreset(presetId, BlockPresetType.Focus);
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult(Guid.NewGuid(), [new(Guid.NewGuid(), 60)], At.Slot(Start, End)));

        PlannedBlock? saved = null;
        await _blocks.AddAsync(Arg.Do<PlannedBlock>(b => saved = b), Arg.Any<CancellationToken>());

        var result = await CreateHandler().HandleAsync(FocusCommand(presetId));

        result.RedemptionCapped.Should().BeFalse();
        saved!.Scheduled.Should().Be(At.Slot(Start, End));
    }
}
