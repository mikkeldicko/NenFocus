using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetAllBlockProfiles;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetAllBlockProfilesQueryHandlerTests
{
    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();

    private GetAllBlockProfilesQueryHandler CreateHandler() => new(_profiles);

    private static BlockProfile CreateProfile(string name, BlockType type)
        => BlockProfile.Create(Guid.NewGuid(), name, WebsitePolicy.Blocklist, type, DateTime.UtcNow);

    [Fact]
    public async Task HandleAsync_NoTagFilter_ReturnsAllProfiles()
    {
        _profiles.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
            (IReadOnlyList<BlockProfile>)[CreateProfile("A", BlockType.Focus), CreateProfile("B", BlockType.Background)]);

        var result = await CreateHandler().HandleAsync(new GetAllBlockProfilesQuery());

        result.Select(p => p.Name).Should().BeEquivalentTo(["A", "B"]);
    }

    [Fact]
    public async Task HandleAsync_WithTag_QueriesByTagInsteadOfAll()
    {
        _profiles.GetByTagsAsync(Arg.Is<IEnumerable<string>>(t => t.Contains("work")), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<BlockProfile>)[CreateProfile("Work Profile", BlockType.Focus)]);

        var result = await CreateHandler().HandleAsync(new GetAllBlockProfilesQuery { Tag = "work" });

        result.Should().ContainSingle(p => p.Name == "Work Profile");
        await _profiles.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithProfileTypeFilter_ReturnsOnlyMatchingType()
    {
        _profiles.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
            (IReadOnlyList<BlockProfile>)[CreateProfile("Focus One", BlockType.Focus), CreateProfile("Bg One", BlockType.Background)]);

        var result = await CreateHandler().HandleAsync(new GetAllBlockProfilesQuery { ProfileType = BlockType.Background });

        result.Should().ContainSingle(p => p.Name == "Bg One");
    }
}
