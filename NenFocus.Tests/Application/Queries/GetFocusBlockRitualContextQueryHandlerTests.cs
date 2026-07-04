using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetFocusBlockRitualContext;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetFocusBlockRitualContextQueryHandlerTests
{
    private static readonly DateTime Start = new(2026, 1, 2, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 2, 15, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();

    private GetFocusBlockRitualContextQueryHandler CreateHandler() => new(_blocks, _profiles);

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
    public async Task HandleAsync_ProfileNotFound_ReturnsNull()
    {
        var blockId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, profileId, Start, End));
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns((BlockProfile?)null);

        var result = await CreateHandler().HandleAsync(
            new GetFocusBlockRitualContextQuery { FocusBlockId = blockId }, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_HappyPath_ReturnsProfileAndSchedule()
    {
        var blockId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, profileId, Start, End));
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, Start));

        var result = await CreateHandler().HandleAsync(
            new GetFocusBlockRitualContextQuery { FocusBlockId = blockId }, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Profile.Name.Should().Be("Deep Work");
        result.Schedule.Start.Should().Be(Start);
        result.Schedule.End.Should().Be(End);
    }
}
