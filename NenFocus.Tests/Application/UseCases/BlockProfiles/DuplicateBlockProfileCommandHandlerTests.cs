using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockProfiles.DuplicateBlockProfile;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.BlockProfiles;

public class DuplicateBlockProfileCommandHandlerTests
{
    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();

    private DuplicateBlockProfileCommandHandler CreateHandler() => new(_profiles);

    private static BlockProfile CreateSourceProfile(Guid id)
        => BlockProfile.Create(id, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow);

    [Fact]
    public async Task HandleAsync_SourceNotFound_Fails()
    {
        _profiles.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((BlockProfile?)null);

        var result = await CreateHandler().HandleAsync(
            new DuplicateBlockProfileCommand { SourceProfileId = Guid.NewGuid() });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WithNewName_UsesProvidedName()
    {
        var sourceId = Guid.NewGuid();
        _profiles.GetByIdAsync(sourceId, Arg.Any<CancellationToken>()).Returns(CreateSourceProfile(sourceId));

        var result = await CreateHandler().HandleAsync(
            new DuplicateBlockProfileCommand { SourceProfileId = sourceId, NewName = "Weekend Mode" });

        result.Success.Should().BeTrue();
        await _profiles.Received(1).AddAsync(
            Arg.Is<BlockProfile>(p => p.Name == "Weekend Mode"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithoutNewName_DefaultsToCopyOfSourceName()
    {
        var sourceId = Guid.NewGuid();
        _profiles.GetByIdAsync(sourceId, Arg.Any<CancellationToken>()).Returns(CreateSourceProfile(sourceId));

        await CreateHandler().HandleAsync(new DuplicateBlockProfileCommand { SourceProfileId = sourceId });

        await _profiles.Received(1).AddAsync(
            Arg.Is<BlockProfile>(p => p.Name == "Copy of Deep Work"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NameCollision_AppendsTimestampSuffix()
    {
        var sourceId = Guid.NewGuid();
        _profiles.GetByIdAsync(sourceId, Arg.Any<CancellationToken>()).Returns(CreateSourceProfile(sourceId));
        _profiles.ExistsWithNameAsync("Copy of Deep Work", cancellationToken: Arg.Any<CancellationToken>())
            .Returns(true);

        await CreateHandler().HandleAsync(new DuplicateBlockProfileCommand { SourceProfileId = sourceId });

        await _profiles.Received(1).AddAsync(
            Arg.Is<BlockProfile>(p => p.Name.StartsWith("Copy of Deep Work (") && p.Name.EndsWith(")")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_HappyPath_ReturnsNewProfileId()
    {
        var sourceId = Guid.NewGuid();
        _profiles.GetByIdAsync(sourceId, Arg.Any<CancellationToken>()).Returns(CreateSourceProfile(sourceId));

        var result = await CreateHandler().HandleAsync(new DuplicateBlockProfileCommand { SourceProfileId = sourceId });

        result.NewProfileId.Should().NotBeNull();
        result.NewProfileId.Should().NotBe(sourceId);
    }
}
