using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockProfiles.DeleteBlockProfile;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.BlockProfiles;

public class DeleteBlockProfileCommandHandlerTests
{
    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();

    private DeleteBlockProfileCommandHandler CreateHandler() => new(_profiles);

    [Fact]
    public async Task HandleAsync_HappyPath_DeletesProfile()
    {
        var profileId = Guid.NewGuid();

        var result = await CreateHandler().HandleAsync(new DeleteBlockProfileCommand { ProfileId = profileId });

        result.Success.Should().BeTrue();
        await _profiles.Received(1).DeleteAsync(profileId, Arg.Any<CancellationToken>());
    }
}
