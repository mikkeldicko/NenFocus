using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Sessions.StartSession;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Sessions;

public class StartSessionValidatorTests
{
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly IBreakTimerPresetRepository _breakTimerPresets = Substitute.For<IBreakTimerPresetRepository>();

    private StartSessionValidator CreateValidator() => new(_presets, _breakTimerPresets);

    private static StartSessionCommand ValidCommand(Guid presetId) => new()
    {
        Goal = "Deep work",
        PresetId = presetId,
        DurationMinutes = 30
    };

    [Fact]
    public async Task Validate_ValidSpontaneousCommandWithFocusPreset_IsValid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow));

        var result = await CreateValidator().ValidateAsync(ValidCommand(presetId));

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
    public async Task Validate_NullPresetId_IsInvalid()
    {
        var command = ValidCommand(Guid.NewGuid()) with { PresetId = null };

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
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow));
        var command = ValidCommand(presetId) with { DurationMinutes = minutes };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_Spontaneous_PresetDoesNotExist_IsInvalid()
    {
        // A stale/deleted PresetId must not silently start a session with no real
        // blocking behind it — GetByIdAsync unstubbed already returns null, matching
        // "doesn't exist."
        var result = await CreateValidator().ValidateAsync(ValidCommand(Guid.NewGuid()));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_Planned_PresetDoesNotExist_SkipsExistenceCheck()
    {
        // Same relationship as the Focus-type check below: a Planned session's preset was
        // already validated at scheduling time, so this rule doesn't apply to it either.
        var command = ValidCommand(Guid.NewGuid()) with { OriginatingBlockId = Guid.NewGuid() };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_Spontaneous_WithBackgroundPreset_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Background", WebsiteFilterType.Blocklist, BlockPresetType.Background, DateTime.UtcNow));

        var result = await CreateValidator().ValidateAsync(ValidCommand(presetId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_Planned_WithBackgroundPreset_SkipsPresetTypeCheck()
    {
        // When OriginatingBlockId is set, the preset was already validated at
        // scheduling time, so the Focus-only check is intentionally skipped.
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Background", WebsiteFilterType.Blocklist, BlockPresetType.Background, DateTime.UtcNow));
        var command = ValidCommand(presetId) with { OriginatingBlockId = Guid.NewGuid() };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_BreakTimerPresetDoesNotExist_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow));
        // _breakTimerPresets.GetByIdAsync unstubbed already returns null, matching
        // "doesn't exist" — same defense-in-depth reasoning as the block preset check.
        var command = ValidCommand(presetId) with { BreakTimerPresetId = Guid.NewGuid() };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BreakTimerPresetExists_IsValid()
    {
        var presetId = Guid.NewGuid();
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow));
        var breakTimerPresetId = Guid.NewGuid();
        _breakTimerPresets.GetByIdAsync(breakTimerPresetId, Arg.Any<CancellationToken>())
            .Returns(BreakTimerPreset.Create(breakTimerPresetId, "Classic", new BreakTimer(25, 5, 4, 15)));
        var command = ValidCommand(presetId) with { BreakTimerPresetId = breakTimerPresetId };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }
}
