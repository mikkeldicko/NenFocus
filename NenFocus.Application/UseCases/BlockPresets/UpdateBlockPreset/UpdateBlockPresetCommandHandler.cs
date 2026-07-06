using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.BlockPresets.UpdateBlockPreset;

public sealed class UpdateBlockPresetCommandHandler
    : ICommandHandler<UpdateBlockPresetCommand, UpdateBlockPresetResult>
{
    private readonly IBlockPresetRepository _presets;

    public UpdateBlockPresetCommandHandler(IBlockPresetRepository presets)
    {
        _presets = presets;
    }

    public async Task<UpdateBlockPresetResult> HandleAsync(
        UpdateBlockPresetCommand command,
        CancellationToken cancellationToken = default)
    {
        var preset = await _presets.GetByIdAsync(command.PresetId, cancellationToken);
        if (preset is null)
            return UpdateBlockPresetResult.Fail($"Preset '{command.PresetId}' not found.");

        if (await _presets.ExistsWithNameAsync(command.Name, excludeId: command.PresetId, cancellationToken: cancellationToken))
            return UpdateBlockPresetResult.Fail($"A preset named '{command.Name}' already exists.");

        preset.Rename(command.Name);
        preset.ChangeWebsiteFilterType(command.WebsiteFilterType);
        if (command.PresetType != preset.PresetType)
            preset.ChangePresetType(command.PresetType);

        foreach (var existing in preset.BlockedApps.ToList()) preset.RemoveBlockedApp(existing);
        foreach (var appName in command.BlockedApps) preset.AddBlockedApp(new BlockedApp(appName));

        foreach (var existing in preset.Websites.ToList()) preset.RemoveWebsite(existing);
        foreach (var url in command.Websites) preset.AddWebsite(new BlockedSite(url));

        foreach (var existing in preset.Tags.ToList()) preset.RemoveTag(existing);
        foreach (var tag in command.Tags) preset.AddTag(tag);

        await _presets.UpdateAsync(preset, cancellationToken);
        return UpdateBlockPresetResult.Ok();
    }
}
