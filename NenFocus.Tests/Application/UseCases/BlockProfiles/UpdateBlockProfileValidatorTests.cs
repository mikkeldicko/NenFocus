using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockProfiles.UpdateBlockProfile;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.BlockProfiles;

public class UpdateBlockProfileValidatorTests
{
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IPlannedBlockRepository _plannedBlocks = Substitute.For<IPlannedBlockRepository>();

    public UpdateBlockProfileValidatorTests()
    {
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
        _plannedBlocks.HasFutureBlocksForProfileAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);
    }

    private UpdateBlockProfileValidator CreateValidator() => new(_sessions, _plannedBlocks);

    private static UpdateBlockProfileCommand ValidCommand(Guid profileId) => new()
    {
        ProfileId = profileId,
        Name = "New Name",
        WebsitePolicy = WebsitePolicy.Blocklist,
        ProfileType = BlockType.Focus,
        BlockedApps = ["chrome"]
    };

    [Fact]
    public async Task Validate_ValidCommand_IsValid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand(Guid.NewGuid()));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ProfileInUseByActiveSession_IsInvalid()
    {
        var profileId = Guid.NewGuid();
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            profileId, SessionOrigin.Spontaneous);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateValidator().ValidateAsync(ValidCommand(profileId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ProfileHasFutureBlocks_IsInvalid()
    {
        var profileId = Guid.NewGuid();
        _plannedBlocks.HasFutureBlocksForProfileAsync(profileId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await CreateValidator().ValidateAsync(ValidCommand(profileId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_UnkillableProcess_IsInvalid()
    {
        var command = ValidCommand(Guid.NewGuid()) with { BlockedApps = ["winlogon"] };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }
}
