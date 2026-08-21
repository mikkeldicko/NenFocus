using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

public class CreatePlannedBlockCommandHandlerTests
{
    private static readonly DateTime Start = new(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 2, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public CreatePlannedBlockCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _blocks.GetOverlappingAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
    }

    private CreatePlannedBlockCommandHandler CreateHandler() => new(_blocks, _presets, _sessions, _clock);

    private static CreatePlannedBlockCommand FocusCommand(Guid presetId) => new()
    {
        PresetId = presetId,
        BlockPresetType = BlockPresetType.Focus,
        Start = Start,
        End = End
    };

    [Fact]
    public async Task HandleAsync_PresetNotFound_Fails()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);

        var result = await CreateHandler().HandleAsync(FocusCommand(presetId));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_Focus_OverlappingFocusBlockExists_Fails()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow));
        _blocks.GetOverlappingAsync(Start.ToUniversalTime(), End.ToUniversalTime(), Now, true, null, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[FocusBlock.Create(Guid.NewGuid(), presetId, Start, End)]);

        var result = await CreateHandler().HandleAsync(FocusCommand(presetId));

        result.Success.Should().BeFalse();
        await _blocks.DidNotReceive().AddAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Focus_PassesCurrentTimeAsOfToOverlapCheck()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow));

        await CreateHandler().HandleAsync(FocusCommand(presetId));

        await _blocks.Received(1).GetOverlappingAsync(
            Start.ToUniversalTime(), End.ToUniversalTime(), Now, true, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Focus_OverlapsActiveSession_Fails()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow));
        var activeSession = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromHours(2)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        activeSession.Start(Start.AddMinutes(-30));
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(activeSession);

        var result = await CreateHandler().HandleAsync(FocusCommand(presetId));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already an active focus session");
    }

    [Fact]
    public async Task HandleAsync_Focus_HappyPath_CreatesFocusBlock()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow));

        var result = await CreateHandler().HandleAsync(FocusCommand(presetId));

        result.Success.Should().BeTrue();
        result.BlockId.Should().NotBeNull();
        await _blocks.Received(1).AddAsync(Arg.Is<PlannedBlock>(b => b is FocusBlock), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Background_SkipsOverlapAndActiveSessionChecks()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Background", WebsiteFilterType.Blocklist, BlockPresetType.Background, DateTime.UtcNow));
        var command = FocusCommand(presetId) with { BlockPresetType = BlockPresetType.Background };

        var result = await CreateHandler().HandleAsync(command);

        result.Success.Should().BeTrue();
        await _blocks.Received(1).AddAsync(Arg.Is<PlannedBlock>(b => b is BackgroundBlock), Arg.Any<CancellationToken>());
        await _blocks.DidNotReceive().GetOverlappingAsync(
            Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithContractId_LinksTheCreatedBlock()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow));
        var contractId = Guid.NewGuid();

        PlannedBlock? saved = null;
        await _blocks.AddAsync(Arg.Do<PlannedBlock>(b => saved = b), Arg.Any<CancellationToken>());

        await CreateHandler().HandleAsync(FocusCommand(presetId) with { ContractId = contractId });

        saved!.ContractId.Should().Be(contractId);
    }

    [Fact]
    public async Task HandleAsync_WithRedemptionEntries_SetsThemOnTheFocusBlock()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow));
        var missedBlockId = Guid.NewGuid();

        PlannedBlock? saved = null;
        await _blocks.AddAsync(Arg.Do<PlannedBlock>(b => saved = b), Arg.Any<CancellationToken>());

        await CreateHandler().HandleAsync(FocusCommand(presetId) with
        {
            RedemptionEntries = [new(missedBlockId, 45)]
        });

        var focusBlock = saved.Should().BeOfType<FocusBlock>().Subject;
        focusBlock.RedemptionEntries.Should().ContainSingle(e => e.MissedBlockId == missedBlockId && e.MinutesCovered == 45);
    }
}
