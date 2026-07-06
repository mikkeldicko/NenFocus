using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockPresets.UpdateBlockPreset;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.BlockPresets;

public class UpdateBlockPresetValidatorTests
{
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IPlannedBlockRepository _plannedBlocks = Substitute.For<IPlannedBlockRepository>();

    public UpdateBlockPresetValidatorTests()
    {
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
        _plannedBlocks.HasFutureBlocksForPresetAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);
    }

    private UpdateBlockPresetValidator CreateValidator() => new(_sessions, _plannedBlocks);

    private static UpdateBlockPresetCommand ValidCommand(Guid presetId) => new()
    {
        PresetId = presetId,
        Name = "New Name",
        WebsiteFilterType = WebsiteFilterType.Blocklist,
        PresetType = BlockPresetType.Focus,
        BlockedApps = ["chrome"]
    };

    [Fact]
    public async Task Validate_ValidCommand_IsValid()
    {
        var result = await CreateValidator().ValidateAsync(ValidCommand(Guid.NewGuid()));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_PresetInUseByActiveSession_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            presetId, SessionOrigin.Spontaneous);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateValidator().ValidateAsync(ValidCommand(presetId));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_PresetHasFutureBlocks_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        _plannedBlocks.HasFutureBlocksForPresetAsync(presetId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await CreateValidator().ValidateAsync(ValidCommand(presetId));

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
