using FluentAssertions;
using NenFocus.Application.Interfaces;
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

    public UpdatePlannedBlockCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _blocks.GetOverlappingAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
    }

    private UpdatePlannedBlockCommandHandler CreateHandler() => new(_blocks, _presets, _sessions, _clock);

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
    public async Task HandleAsync_FocusBlock_ReplacesMakeUpForEntries()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var block = FocusBlock.Create(blockId, presetId, Start, End,
            makeUpForEntries: [new MakeUpForEntry(Guid.NewGuid(), 20)]);
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));
        var newMissedBlockId = Guid.NewGuid();

        var result = await CreateHandler().HandleAsync(
            ValidCommand(blockId, presetId) with { MakeUpForEntries = [new(newMissedBlockId, 40)] });

        result.Success.Should().BeTrue();
        block.MakeUpForEntries.Should().ContainSingle(e => e.MissedBlockId == newMissedBlockId && e.MinutesCovered == 40);
    }

    [Fact]
    public async Task HandleAsync_FocusBlock_EmptyMakeUpForEntries_ClearsExisting()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var block = FocusBlock.Create(blockId, presetId, Start, End,
            makeUpForEntries: [new MakeUpForEntry(Guid.NewGuid(), 20)]);
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(block);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns(FocusPreset(presetId));

        await CreateHandler().HandleAsync(ValidCommand(blockId, presetId));

        block.MakeUpForEntries.Should().BeEmpty();
    }
}
