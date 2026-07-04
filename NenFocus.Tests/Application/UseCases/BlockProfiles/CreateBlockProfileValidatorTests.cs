using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockProfiles.CreateBlockProfile;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.BlockProfiles;

public class CreateBlockProfileValidatorTests
{
    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();

    private CreateBlockProfileValidator CreateValidator() => new(_profiles);

    private static CreateBlockProfileCommand ValidCommand() => new()
    {
        Name = "Deep Work",
        WebsitePolicy = WebsitePolicy.Blocklist,
        ProfileType = BlockType.Focus,
        BlockedApps = ["chrome"]
    };

    [Fact]
    public async Task Validate_ValidCommand_IsValid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyName_IsInvalid()
    {
        var command = ValidCommand() with { Name = "" };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_EmptyName_ErrorMessageIsRequiredMessage()
    {
        var command = ValidCommand() with { Name = "" };

        var result = await CreateValidator().ValidateAsync(command);

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateBlockProfileCommand.Name))
            .Which.ErrorMessage.Should().Be("Profile name is required.");
    }

    [Fact]
    public async Task Validate_NoAppsAndNoWebsites_IsInvalid()
    {
        var command = ValidCommand() with { BlockedApps = [], Websites = [] };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_UnkillableProcess_IsInvalid()
    {
        var command = ValidCommand() with { BlockedApps = ["explorer.exe"] };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_TagOverMaxLength_IsInvalid()
    {
        var command = ValidCommand() with { Tags = [new string('a', 31)] };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }
}
