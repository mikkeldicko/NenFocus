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
    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();

    private GetPlannedBlocksForDayQueryHandler CreateHandler() => new(_blocks, _profiles);

    [Fact]
    public async Task HandleAsync_FocusBlock_MapsProfileNameAndBlockType()
    {
        var profileId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), profileId, Start, End);
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _profiles.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
            (IReadOnlyList<BlockProfile>)[BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Allowlist, BlockType.Focus, Start)]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.ProfileName.Should().Be("Deep Work");
        dto.BlockType.Should().Be(BlockType.Focus);
        dto.WebsitePolicy.Should().Be(WebsitePolicy.Allowlist);
        dto.Start.Should().Be(Start);
        dto.End.Should().Be(End);
        dto.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_BackgroundBlock_MapsBlockType()
    {
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _profiles.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockProfile>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.BlockType.Should().Be(BlockType.Background);
    }

    [Fact]
    public async Task HandleAsync_ProfileNotFound_UsesUnknownNameAndBlocklistDefault()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _profiles.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockProfile>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        var dto = result.Should().ContainSingle().Subject;
        dto.ProfileName.Should().Be("Unknown");
        dto.WebsitePolicy.Should().Be(WebsitePolicy.Blocklist);
    }

    [Fact]
    public async Task HandleAsync_FocusBlockWithCompletedRitual_IsCompletedIsTrue()
    {
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End);
        block.MarkRitualCompleted(Start);
        _blocks.GetForDayAsync(Day, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _profiles.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<BlockProfile>)[]);

        var result = await CreateHandler().HandleAsync(new GetPlannedBlocksForDayQuery { Date = Day });

        result.Should().ContainSingle().Subject.IsCompleted.Should().BeTrue();
    }
}
