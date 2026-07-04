using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockProfiles.DeleteBlockProfile;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.BlockProfiles;

public class DeleteBlockProfileValidatorTests
{
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IPlannedBlockRepository _plannedBlocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public DeleteBlockProfileValidatorTests()
    {
        _clock.UtcNow.Returns(DateTime.UtcNow);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
        _plannedBlocks.HasFutureBlocksForProfileAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);
    }

    private DeleteBlockProfileValidator CreateValidator() => new(_sessions, _plannedBlocks, _clock);

    [Fact]
    public async Task Validate_NotInUse_IsValid()
    {
        var result = await CreateValidator().ValidateAsync(new DeleteBlockProfileCommand { ProfileId = Guid.NewGuid() });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_InUseByActiveSession_IsInvalid()
    {
        var profileId = Guid.NewGuid();
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            profileId, SessionOrigin.Spontaneous);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateValidator().ValidateAsync(new DeleteBlockProfileCommand { ProfileId = profileId });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_HasFutureBlocks_IsInvalid()
    {
        var profileId = Guid.NewGuid();
        _plannedBlocks.HasFutureBlocksForProfileAsync(profileId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await CreateValidator().ValidateAsync(new DeleteBlockProfileCommand { ProfileId = profileId });

        result.IsValid.Should().BeFalse();
    }
}
