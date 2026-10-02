using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

// Placement ("is this time free", "in the past", "inside the contract's hours") is the
// validator's job via GetBlockPlacementVerdictQuery since Gap 88 phase 8 — tested in
// GetBlockPlacementVerdictQueryHandlerTests. The cap is attribution's (its own tests).
// The handler saves what attribution returns, with the membership the contract the slot sits
// in hands out (Gap 97).
public class CreatePlannedBlockCommandHandlerTests
{
    private static readonly DateTime Start = new(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 2, 11, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> _computeRedemptionAttribution =
        Substitute.For<IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult>>();
    private readonly ISystemClock _clock = TestClock.Create();

    public CreatePlannedBlockCommandHandlerTests()
    {
        _clock.SetNow(Start.AddHours(-1));
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[]);

        // Default: no redemption entries, the requested slot unchanged — the real query's
        // answer when nothing is eligible to cover.
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var query = (ComputeRedemptionAttributionQuery)ci[0];
                return new RedemptionAttributionResult([], query.Block);
            });
    }

    private CreatePlannedBlockCommandHandler CreateHandler() =>
        new(_blocks, _presets, _contracts, _computeRedemptionAttribution, new BlockPlacementChecker(), _clock);

    /// <summary>A contract in effect whose period (the whole of 2–9 January) contains the command's slot.</summary>
    private Guid SetUpContractAroundTheSlot()
    {
        var contract = TestContracts.Seal(
            id: Guid.NewGuid(),
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(DateOnly.FromDateTime(Start), DateOnly.FromDateTime(Start).AddDays(7), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Start.Date), timeZone: At.UtcZone);
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);
        return contract.Id;
    }

    private async Task<PlannedBlock> SavedBlockAsync(CreatePlannedBlockCommand command)
    {
        PlannedBlock? saved = null;
        await _blocks.AddAsync(Arg.Do<PlannedBlock>(b => saved = b), Arg.Any<CancellationToken>());

        (await CreateHandler().HandleAsync(command)).Success.Should().BeTrue();

        return saved!;
    }

    private static CreatePlannedBlockCommand FocusCommand(Guid presetId) => new()
    {
        PresetId = presetId,
        BlockPresetType = BlockPresetType.Focus,
        Slot = At.Slot(Start, End)
    };

    private void SetUpPreset(Guid presetId, BlockPresetType type) =>
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Preset", WebsiteFilterType.Blocklist, type, At.Utc(Start.AddDays(-1))));

    private Guid FocusPreset()
    {
        var presetId = Guid.NewGuid();
        SetUpPreset(presetId, BlockPresetType.Focus);
        return presetId;
    }

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
        var result = await CreateHandler().HandleAsync(FocusCommand(FocusPreset()));

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

    // ── Membership comes from where the slot sits (Gap 97) ───────────────────

    [Fact]
    public async Task HandleAsync_InsideNoContract_IsAPlainBlock_AndAttributionIsNeverAsked()
    {
        var saved = await SavedBlockAsync(FocusCommand(FocusPreset()));

        saved.Membership.Should().BeNull();
        await _computeRedemptionAttribution.DidNotReceive().HandleAsync(
            Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_InsideAContract_NoDeficitCovered_IsABonusBlock()
    {
        var contractId = SetUpContractAroundTheSlot();

        var saved = await SavedBlockAsync(FocusCommand(FocusPreset()));

        saved.Membership.Should().Be(TestContracts.Bonus(contractId));
    }

    [Fact]
    public async Task HandleAsync_InsideAContract_CoversADeficit_IsARedemptionBlock_AskingOnlyThatContract()
    {
        var contractId = SetUpContractAroundTheSlot();
        var missedBlockId = Guid.NewGuid();
        _computeRedemptionAttribution
            .HandleAsync(Arg.Is<ComputeRedemptionAttributionQuery>(q => q.ContractId == contractId), Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult([new(missedBlockId, 60)], At.Slot(Start, End)));

        var saved = await SavedBlockAsync(FocusCommand(FocusPreset()));

        var focusBlock = saved.Should().BeOfType<FocusBlock>().Subject;
        focusBlock.Membership.Should().Be(TestContracts.Redemption(contractId));
        focusBlock.RedemptionEntries.Should().ContainSingle(e => e.MissedBlockId == missedBlockId && e.MinutesCovered == 60);
    }

    [Fact]
    public async Task HandleAsync_InsideAContract_DeclineRedemption_SkipsAttribution_IsABonusBlockAtTheRequestedSlot()
    {
        var contractId = SetUpContractAroundTheSlot();
        // A real match exists — proves DeclineRedemption skips it rather than the match
        // simply not being found.
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult([new(Guid.NewGuid(), 45)], At.Slot(Start, Start.AddMinutes(45)), true));

        var saved = await SavedBlockAsync(FocusCommand(FocusPreset()) with { DeclineRedemption = true });

        await _computeRedemptionAttribution.DidNotReceive().HandleAsync(
            Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>());
        var focusBlock = saved.Should().BeOfType<FocusBlock>().Subject;
        focusBlock.Membership.Should().Be(TestContracts.Bonus(contractId));
        focusBlock.RedemptionEntries.Should().BeEmpty();
        focusBlock.Scheduled.Should().Be(At.Slot(Start, End));
    }

    [Fact]
    public async Task HandleAsync_Background_InsideAContract_IsABonusBlock_AndNeverConsultsAttribution()
    {
        var contractId = SetUpContractAroundTheSlot();
        var presetId = Guid.NewGuid();
        SetUpPreset(presetId, BlockPresetType.Background);

        var saved = await SavedBlockAsync(FocusCommand(presetId) with { BlockPresetType = BlockPresetType.Background });

        saved.Membership.Should().Be(TestContracts.Bonus(contractId));
        await _computeRedemptionAttribution.DidNotReceive().HandleAsync(
            Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>());
    }

    // ── The slot saved ───────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Focus_AttributionReturnsACappedSlot_SavesItAndReportsCapped()
    {
        SetUpContractAroundTheSlot();
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult([new(Guid.NewGuid(), 45)], At.Slot(Start, Start.AddMinutes(45)), true));

        PlannedBlock? saved = null;
        await _blocks.AddAsync(Arg.Do<PlannedBlock>(b => saved = b), Arg.Any<CancellationToken>());

        var command = FocusCommand(FocusPreset()) with { Slot = At.Slot(Start, Start.AddHours(2)) };
        var result = await CreateHandler().HandleAsync(command);

        result.Success.Should().BeTrue();
        result.RedemptionCapped.Should().BeTrue();
        saved!.Scheduled.Should().Be(At.Slot(Start, Start.AddMinutes(45)));
    }

    [Fact]
    public async Task HandleAsync_Focus_AttributionReturnsTheRequestedSlot_DoesNotReportCapped()
    {
        SetUpContractAroundTheSlot();
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult([new(Guid.NewGuid(), 60)], At.Slot(Start, End)));

        PlannedBlock? saved = null;
        await _blocks.AddAsync(Arg.Do<PlannedBlock>(b => saved = b), Arg.Any<CancellationToken>());

        var result = await CreateHandler().HandleAsync(FocusCommand(FocusPreset()));

        result.RedemptionCapped.Should().BeFalse();
        saved!.Scheduled.Should().Be(At.Slot(Start, End));
    }
}
