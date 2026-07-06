using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockPresets.CreateBlockPreset;
using NenFocus.Domain.Enums;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.BlockPresets;

public class CreateBlockPresetValidatorTests
{
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();

    private CreateBlockPresetValidator CreateValidator() => new(_presets);

    private static CreateBlockPresetCommand ValidCommand() => new()
    {
        Name = "Deep Work",
        WebsiteFilterType = WebsiteFilterType.Blocklist,
        PresetType = BlockPresetType.Focus,
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

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateBlockPresetCommand.Name))
            .Which.ErrorMessage.Should().Be("Preset name is required.");
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
