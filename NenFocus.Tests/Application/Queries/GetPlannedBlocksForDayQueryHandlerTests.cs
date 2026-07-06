using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetPlannedBlocksForDay;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetPlannedBlocksForDayQueryHandlerTests
{
    private static readonly DateTime Start = new(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 2, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2026, 1, 2);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();

    private GetPlannedBlocksForDayQueryHandler CreateHandler() => new(_blocks, _presets);

    [Fact]
    public async Task HandleAsync_FocusBlock_MapsPresetNameAndBlockPresetType()
    {
        var presetId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), presetId, Start, End);
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
            (IReadOnlyList<BlockPreset>)[BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Allowlist, BlockPresetType.Focus, Start)]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.PresetName.Should().Be("Deep Work");
        dto.BlockPresetType.Should().Be(BlockPresetType.Focus);
        dto.WebsiteFilterType.Should().Be(WebsiteFilterType.Allowlist);
        dto.Start.Should().Be(Start);
        dto.End.Should().Be(End);
        dto.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_BackgroundBlock_MapsBlockPresetType()
    {
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.BlockPresetType.Should().Be(BlockPresetType.Background);
    }

    [Fact]
    public async Task HandleAsync_PresetNotFound_UsesUnknownNameAndBlocklistDefault()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.PresetName.Should().Be("Unknown");
        dto.WebsiteFilterType.Should().Be(WebsiteFilterType.Blocklist);
    }

    [Fact]
    public async Task HandleAsync_FocusBlockWithCompletedRitual_IsCompletedIsTrue()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        block.MarkRitualCompleted(Start);
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _presets.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockPreset>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.IsCompleted.Should().BeTrue();
    }
}
