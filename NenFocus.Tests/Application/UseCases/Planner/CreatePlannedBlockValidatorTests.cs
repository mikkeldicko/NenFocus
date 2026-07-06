using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

public class CreatePlannedBlockValidatorTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public CreatePlannedBlockValidatorTests() => _clock.UtcNow.Returns(Now);

    private CreatePlannedBlockValidator CreateValidator() => new(_profiles, _clock);

    private static CreatePlannedBlockCommand ValidCommand(Guid profileId) => new()
    {
        ProfileId = profileId,
        BlockType = BlockType.Focus,
        Start = Now.AddHours(1),
        End = Now.AddHours(2)
    };

    [Fact]
    public async Task Validate_ValidCommand_IsValid()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, Now));

        var result = await CreateValidator().ValidateAsync(ValidCommand(profileId));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyProfileId_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand(Guid.Empty));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ProfileTypeMismatch_IsInvalid()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Background", WebsitePolicy.Blocklist, BlockType.Background, Now));

        var result = await CreateValidator().ValidateAsync(ValidCommand(profileId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_StartInPast_IsInvalid()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, Now));
        var command = ValidCommand(profileId) with { Start = Now.AddMinutes(-10) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_EndBeforeStart_IsInvalid()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, Now));
        var command = ValidCommand(profileId) with { End = Now.AddMinutes(30) - TimeSpan.FromHours(2) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ShorterThanOneMinute_IsInvalid()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, Now));
        var command = ValidCommand(profileId) with { End = ValidCommand(profileId).Start.AddSeconds(30) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ExactlyOneMinute_IsValid()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, Now));
        var command = ValidCommand(profileId) with { End = ValidCommand(profileId).Start.AddMinutes(1) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }
}
