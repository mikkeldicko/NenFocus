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

        if (await _presets.ExistsWithNameAsync(command.Name, command.PresetType, excludeId: command.PresetId, cancellationToken: cancellationToken))
            return UpdateBlockPresetResult.Fail($"A preset named '{command.Name}' already exists.");

        // preset was loaded by this same DbContext (tracked), and TransactionalCommandHandler
        // commits unconditionally regardless of this handler's Result — so everything that
        // can throw must be validated/constructed before any mutation of preset begins.
        // A partial mutation left in place by a mid-sequence exception would still get
        // persisted. Rename included here: its own guard is check-then-assign, so it cannot
        // partially mutate either.
        List<BlockedApp> newApps;
        List<BlockedSite> newSites;
        try
        {
            newApps = command.BlockedApps.Select(a => new BlockedApp(a)).ToList();
            newSites = command.Websites.Select(u => new BlockedSite(u)).ToList();
            preset.Rename(command.Name);
        }
        catch (ArgumentException ex)
        {
            return UpdateBlockPresetResult.Fail(ex.Message);
        }

        preset.ChangeWebsiteFilterType(command.WebsiteFilterType);
        if (command.PresetType != preset.PresetType)
            preset.ChangePresetType(command.PresetType);

        foreach (var existing in preset.BlockedApps.ToList()) preset.RemoveBlockedApp(existing);
        foreach (var app in newApps) preset.AddBlockedApp(app);

        foreach (var existing in preset.Websites.ToList()) preset.RemoveWebsite(existing);
        foreach (var site in newSites) preset.AddWebsite(site);

        foreach (var existing in preset.Tags.ToList()) preset.RemoveTag(existing);
        foreach (var tag in command.Tags) preset.AddTag(tag);

        await _presets.UpdateAsync(preset, cancellationToken);
        return UpdateBlockPresetResult.Ok();
    }
}
