using FluentAssertions;
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

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    private GetFocusBlockRitualContextQueryHandler CreateHandler() => new(_blocks, _presets, _clock);

    [Fact]
    public async Task HandleAsync_BlockNotFound_ReturnsNull()
    {
        _blocks.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PlannedBlock?)null);

        var result = await CreateHandler().HandleAsync(
            new GetFocusBlockRitualContextQuery { FocusBlockId = Guid.NewGuid() }, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_BlockIsNotAFocusBlock_ReturnsNull()
    {
        var blockId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(BackgroundBlock.Create(blockId, Guid.NewGuid(), Start, End));

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
            .Returns(FocusBlock.Create(blockId, presetId, Start, End));
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
            .Returns(FocusBlock.Create(blockId, presetId, Start, End));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Start));
        _clock.UtcNow.Returns(Start);

        var result = await CreateHandler().HandleAsync(
            new GetFocusBlockRitualContextQuery { FocusBlockId = blockId }, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Preset.Name.Should().Be("Deep Work");
        result.Schedule.Start.Should().Be(Start);
        result.Schedule.End.Should().Be(End);
    }

    [Theory]
    [InlineData(0, false)]   // right on time
    [InlineData(5, false)]   // exactly at the grace period boundary — still within grace
    [InlineData(6, true)]    // past the grace period
    [InlineData(90, true)]
    public async Task HandleAsync_IsLate_ComparesAgainstFocusBlockOutcomeClassifierGracePeriod(
        int minutesAfterStart, bool expectedIsLate)
    {
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Start, End));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Start));
        _clock.UtcNow.Returns(Start.AddMinutes(minutesAfterStart));

        var result = await CreateHandler().HandleAsync(
            new GetFocusBlockRitualContextQuery { FocusBlockId = blockId }, CancellationToken.None);

        result!.IsLate.Should().Be(expectedIsLate);
        result.LateBy.Should().Be(TimeSpan.FromMinutes(minutesAfterStart));
    }

    [Fact]
    public async Task HandleAsync_UsesSameGracePeriodConstantAsFocusBlockOutcomeClassifier()
    {
        // Not a hardcoded "5" — asserts against the shared constant directly, so this
        // test can't silently drift from FocusBlockOutcomeClassifier's own definition.
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, Start, End));
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Start));
        _clock.UtcNow.Returns(Start.AddMinutes(FocusBlockOutcomeClassifier.GracePeriodMinutes + 1));

        var result = await CreateHandler().HandleAsync(
            new GetFocusBlockRitualContextQuery { FocusBlockId = blockId }, CancellationToken.None);

        result!.IsLate.Should().BeTrue();
    }
}
