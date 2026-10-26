using FluentAssertions;
using NenFocus.Domain.ValueObjects;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetFocusBlockRitualContext;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetFocusBlockRitualContextQueryHandlerTests
{
    private static readonly DateTime Start = new(2026, 1, 2, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 2, 15, 0, 0, DateTimeKind.Utc);

    private readonly IBlockRepository _blocks = Substitute.For<IBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly ISystemClock _clock = TestClock.Create();

    private GetFocusBlockRitualContextQueryHandler CreateHandler() => new(_blocks, _presets, _sessions, _clock);

    [Fact]
    public async Task HandleAsync_BlockNotFound_ReturnsNull()
    {
        _blocks.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Block?)null);

        var result = await CreateHandler().HandleAsync(
            new GetFocusBlockRitualContextQuery { FocusBlockId = Guid.NewGuid() }, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_BlockIsNotAFocusBlock_ReturnsNull()
    {
        var blockId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(BackgroundBlock.Create(blockId, Guid.NewGuid(),At.Slot(Start, End)));

        var result = await CreateHandler().HandleAsync(
            new GetFocusBlockRitualContextQuery { FocusBlockId = blockId }, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_PresetNotFound_ReturnsNull()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Start, End)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>()).Returns((BlockPreset?)null);

        var result = await CreateHandler().HandleAsync(
            new GetFocusBlockRitualContextQuery { FocusBlockId = blockId }, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_HappyPath_ReturnsPresetAndSchedule()
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Start, End)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Start)));
        _clock.SetNow(Start);

        var result = await CreateHandler().HandleAsync(
            new GetFocusBlockRitualContextQuery { FocusBlockId = blockId }, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Preset.Name.Should().Be("Deep Work");
        result.Schedule.Slot.Should().Be(At.Slot(Start, End));
        result.AlreadyHasSession.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_SessionAlreadyExistsForBlock_AlreadyHasSessionIsTrue()
    {
        // Confirms the guard ShellViewModel.StartRitual relies on to decline opening the
        // Ritual for a block that already has a resolved Session — e.g. one the sweep
        // marked Missed under a different "now" than whatever triggered this ritual
        // request (see DESIGN_GAP_HISTORY.md Gap 21's addenda). Without this, the user
        // could walk through the entire Ritual only to be rejected at Hatsu.
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Start, End)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Start)));
        _sessions.GetByOriginatingBlockIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(TestSessions.For(blockId));
        _clock.SetNow(Start);

        var result = await CreateHandler().HandleAsync(
            new GetFocusBlockRitualContextQuery { FocusBlockId = blockId }, CancellationToken.None);

        result!.AlreadyHasSession.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, false)]   // right on time
    [InlineData(5, false)]   // exactly at the grace period boundary — still within grace
    [InlineData(6, true)]    // past the grace period
    [InlineData(90, true)]
    public async Task HandleAsync_IsLate_ComparesAgainstTheBlocksGracePeriod(
        int minutesAfterStart, bool expectedIsLate)
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Start, End)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Start)));
        _clock.SetNow(Start.AddMinutes(minutesAfterStart));

        var result = await CreateHandler().HandleAsync(
            new GetFocusBlockRitualContextQuery { FocusBlockId = blockId }, CancellationToken.None);

        result!.IsLate.Should().Be(expectedIsLate);
        result.LateBy.Should().Be(TimeSpan.FromMinutes(minutesAfterStart));
    }

    [Fact]
    public async Task HandleAsync_UsesTheBlocksGracePeriodConstant()
    {
        // Not a hardcoded "5" — asserts against the shared constant directly, so this
        // test can't silently drift from FocusBlock's own definition.
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId,At.Slot(Start, End)));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Start)));
        _clock.SetNow(Start.AddMinutes(FocusBlock.GracePeriodMinutes + 1));

        var result = await CreateHandler().HandleAsync(
            new GetFocusBlockRitualContextQuery { FocusBlockId = blockId }, CancellationToken.None);

        result!.IsLate.Should().BeTrue();
    }
}
