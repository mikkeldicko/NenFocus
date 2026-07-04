using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.StartSession;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Sessions;

public class StartSessionValidatorTests
{
    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();

    private StartSessionValidator CreateValidator() => new(_profiles);

    private static StartSessionCommand ValidCommand(Guid profileId) => new()
    {
        Goal = "Deep work",
        ProfileId = profileId,
        DurationMinutes = 30
    };

    [Fact]
    public async Task Validate_ValidSpontaneousCommandWithFocusProfile_IsValid()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow));

        var result = await CreateValidator().ValidateAsync(ValidCommand(profileId));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyGoal_IsInvalid()
    {
        var command = ValidCommand(Guid.NewGuid()) with { Goal = "" };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_GoalOverMaxLength_IsInvalid()
    {
        var command = ValidCommand(Guid.NewGuid()) with { Goal = new string('a', 501) };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_NullProfileId_IsInvalid()
    {
        var command = ValidCommand(Guid.NewGuid()) with { ProfileId = null };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(481)]
    public async Task Validate_DurationOutOfRange_IsInvalid(int minutes)
    {
        var command = ValidCommand(Guid.NewGuid()) with { DurationMinutes = minutes };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(480)]
    public async Task Validate_DurationAtBoundary_IsValid(int minutes)
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow));
        var command = ValidCommand(profileId) with { DurationMinutes = minutes };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_Spontaneous_WithBackgroundProfile_IsInvalid()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Background", WebsitePolicy.Blocklist, BlockType.Background, DateTime.UtcNow));

        var result = await CreateValidator().ValidateAsync(ValidCommand(profileId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_Planned_WithBackgroundProfile_SkipsProfileTypeCheck()
    {
        // When OriginatingBlockId is set, the profile was already validated at
        // scheduling time, so the Focus-only check is intentionally skipped.
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Background", WebsitePolicy.Blocklist, BlockType.Background, DateTime.UtcNow));
        var command = ValidCommand(profileId) with { OriginatingBlockId = Guid.NewGuid() };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_InvalidBreakInterval_IsInvalid()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow));
        var command = ValidCommand(profileId) with
        {
            BreakSettings = new BreakSettingsDto { IntervalMinutes = 20, BreakDurationMinutes = 5 }
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_InvalidBreakDuration_IsInvalid()
    {
        var profileId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(BlockProfile.Create(profileId, "Deep Work", WebsitePolicy.Blocklist, BlockType.Focus, DateTime.UtcNow));
        var command = ValidCommand(profileId) with
        {
            BreakSettings = new BreakSettingsDto { IntervalMinutes = 25, BreakDurationMinutes = 7 }
        };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }
}
