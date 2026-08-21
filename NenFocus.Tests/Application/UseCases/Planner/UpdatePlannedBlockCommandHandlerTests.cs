using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

public class UpdatePlannedBlockCommandHandlerTests
{
    private static readonly DateTime Start = new(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 2, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();
    private readonly IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> _computeRedemptionAttribution =
        Substitute.For<IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult>>();

    public UpdatePlannedBlockCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _blocks.GetOverlappingAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
        _computeRedemptionAttribution
            .HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci => new RedemptionAttributionResult(((ComputeRedemptionAttributionQuery)ci[0]).ContractId, []));
    }

    private UpdatePlannedBlockCommandHandler CreateHandler() => new(_blocks, _presets, _sessions, _clock, _computeRedemptionAttribution);

    private static UpdatePlannedBlockCommand ValidCommand(Guid blockId, Guid presetId) => new()
    {
        BlockId = blockId,
        PresetId = presetId,
        Start = Start,
        End = End
    };

    private static BlockPreset FocusPreset(Guid id)
        => BlockPreset.Create(id, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow);

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
            .Returns(BackgroundBlock.Create(blockId, Guid.NewGuid(), Start, End));
        _presets.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId, Guid.NewGuid()));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_FocusBlock_OverlapsAnotherFocusBlock_Fails()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Start, End));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));
        _blocks.GetOverlappingAsync(Start, End, Now, true, blockId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[FocusBlock.Create(Guid.NewGuid(), presetId, Start, End)]);

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId, presetId));

        result.Success.Should().BeFalse();
        await _blocks.DidNotReceive().UpdateAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_FocusBlock_OverlapsActiveSession_Fails()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Start, End));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));
        var activeSession = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromHours(2)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        activeSession.Start(Start.AddMinutes(-30));
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(activeSession);

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId, presetId));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already an active focus session");
    }

    [Fact]
    public async Task HandleAsync_ActiveSessionStartedLate_UsesEndAtNotStartedAtPlusDuration()
    {
        // Same fix as CreatePlannedBlockCommandHandler's identical check — see there for
        // the full explanation.
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Start, End));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));
        var activeSession = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromHours(1)),
            Guid.NewGuid(), SessionOrigin.Planned, originatingBlockId: Guid.NewGuid());
        activeSession.Start(Start.AddMinutes(-55), scheduledEnd: Start); // started 5 min "late", EndAt pinned to Start
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(activeSession);

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId, presetId)); // edited block: Start–End

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_BackgroundBlock_SkipsOverlapAndSessionChecks()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(BackgroundBlock.Create(blockId, presetId, Start, End));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Background", WebsiteFilterType.Blocklist, BlockPresetType.Background, DateTime.UtcNow));

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId, presetId));

        result.Success.Should().BeTrue();
        await _blocks.DidNotReceive().GetOverlappingAsync(
            Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_HappyPath_ReschedulesAndReassignsPreset()
    {
        var blockId = Guid.NewGuid();
        var oldPresetId = Guid.NewGuid();
        var newPresetId = Guid.NewGuid();
        var block = FocusBlock.Create(blockId, oldPresetId, Start.AddDays(-1), End.AddDays(-1));
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(newPresetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(newPresetId));

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId, newPresetId));

        result.Success.Should().BeTrue();
        block.Start.Should().Be(Start);
        block.End.Should().Be(End);
        block.BlockPresetId.Should().Be(newPresetId);
        await _blocks.Received(1).UpdateAsync(block, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_FocusBlock_WithContractId_RecomputesRedemptionEntriesFromAttribution()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(blockId, presetId, Start, End, contractId: contractId,
            redemptionEntries: [new RedemptionEntry(Guid.NewGuid(), 20)]);
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));
        var newMissedBlockId = Guid.NewGuid();
        _computeRedemptionAttribution
            .HandleAsync(Arg.Is<ComputeRedemptionAttributionQuery>(q => q.ContractId == contractId && q.ExcludeBlockId == blockId),
                Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult(contractId, [new(newMissedBlockId, 40)]));

        var result = await CreateHandler().HandleAsync(ValidCommand(blockId, presetId));

        result.Success.Should().BeTrue();
        block.RedemptionEntries.Should().ContainSingle(e => e.MissedBlockId == newMissedBlockId && e.MinutesCovered == 40);
    }

    [Fact]
    public async Task HandleAsync_FocusBlock_WithContractId_NoLongerEligible_ClearsExistingEntries()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var block = FocusBlock.Create(blockId, presetId, Start, End, contractId: contractId,
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
        var block = FocusBlock.Create(blockId, presetId, Start, End);
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));

        await CreateHandler().HandleAsync(ValidCommand(blockId, presetId));

        block.RedemptionEntries.Should().BeEmpty();
        await _computeRedemptionAttribution.DidNotReceive().HandleAsync(
            Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>());
    }
}
