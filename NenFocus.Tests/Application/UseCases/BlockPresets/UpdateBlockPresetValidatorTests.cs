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
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public UpdateBlockPresetValidatorTests()
    {
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
        _plannedBlocks.HasFutureBlocksForPresetAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _clock.UtcNow.Returns(DateTime.UtcNow);
    }

    private UpdateBlockPresetValidator CreateValidator() => new(_sessions, _plannedBlocks, _presets, _clock);

    private static BlockPreset ExistingPreset(Guid presetId, IEnumerable<string>? apps = null, IEnumerable<string>? sites = null)
    {
        var preset = BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, DateTime.UtcNow);
        foreach (var app in apps ?? []) preset.AddBlockedApp(new BlockedApp(app));
        foreach (var site in sites ?? []) preset.AddWebsite(new BlockedSite(site));
        return preset;
    }

    private static UpdateBlockPresetCommand ValidCommand(Guid presetId) => new()
    {
        PresetId = presetId,
        Name = "Deep Work",
        WebsiteFilterType = WebsiteFilterType.Blocklist,
        PresetType = BlockPresetType.Focus,
        BlockedApps = ["chrome"]
    };

    private void LockByActiveSession(Guid presetId)
    {
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            presetId, SessionOrigin.Spontaneous);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);
    }

    private void LockByFutureBlock(Guid presetId)
        => _plannedBlocks.HasFutureBlocksForPresetAsync(presetId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);

    [Fact]
    public async Task Validate_NotLocked_AnyChangeIsValid()
    {
        var presetId = Guid.NewGuid();

        var result = await CreateValidator().ValidateAsync(ValidCommand(presetId) with { Name = "Renamed" });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_LockedByActiveSession_AddingAnAppOnly_IsValid()
    {
        var presetId = Guid.NewGuid();
        LockByActiveSession(presetId);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(ExistingPreset(presetId, apps: ["chrome"]));

        var command = ValidCommand(presetId) with { BlockedApps = ["chrome", "firefox"] };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_LockedByFutureBlock_AddingAWebsiteOnly_IsValid()
    {
        var presetId = Guid.NewGuid();
        LockByFutureBlock(presetId);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(ExistingPreset(presetId, sites: ["reddit.com"]));

        var command = ValidCommand(presetId) with { BlockedApps = [], Websites = ["reddit.com", "newsite.com"] };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_LockedAndRemovingAnApp_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        LockByActiveSession(presetId);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(ExistingPreset(presetId, apps: ["chrome", "firefox"]));

        var command = ValidCommand(presetId) with { BlockedApps = ["chrome"] }; // firefox dropped

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_LockedAndRemovingAWebsite_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        LockByFutureBlock(presetId);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(ExistingPreset(presetId, sites: ["reddit.com", "youtube.com"]));

        var command = ValidCommand(presetId); // Websites empty — drops both existing sites

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_LockedAndRenaming_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        LockByActiveSession(presetId);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(ExistingPreset(presetId));

        var command = ValidCommand(presetId) with { Name = "New Name" };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_LockedAndChangingWebsiteFilterType_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        LockByActiveSession(presetId);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(ExistingPreset(presetId));

        var command = ValidCommand(presetId) with { WebsiteFilterType = WebsiteFilterType.Allowlist };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_LockedAndChangingPresetType_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        LockByFutureBlock(presetId);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(ExistingPreset(presetId));

        var command = ValidCommand(presetId) with { PresetType = BlockPresetType.Background };

        var result = await CreateValidator().ValidateAsync(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_LockedAndChangingTags_IsInvalid()
    {
        var presetId = Guid.NewGuid();
        LockByActiveSession(presetId);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(ExistingPreset(presetId));

        var command = ValidCommand(presetId) with { Tags = ["new-tag"] };

        var result = await CreateValidator().ValidateAsync(command);

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
