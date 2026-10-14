using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.BlockPresets.CreateBlockPreset;

public sealed class CreateBlockPresetCommandHandler
    : ICommandHandler<CreateBlockPresetCommand, CreateBlockPresetResult>
{
    private readonly IBlockPresetRepository _presets;
    private readonly ISystemClock _clock;

    public CreateBlockPresetCommandHandler(
        IBlockPresetRepository presets,
        ISystemClock clock)
    {
        _presets = presets;
        _clock = clock;
    }

    public async Task<CreateBlockPresetResult> HandleAsync(
        CreateBlockPresetCommand command,
        CancellationToken cancellationToken = default)
    {
        if (await _presets.ExistsWithNameAsync(command.Name, command.PresetType, cancellationToken: cancellationToken))
            return CreateBlockPresetResult.Fail($"A preset named '{command.Name}' already exists.");

        BlockPreset preset;
        try
        {
            preset = BlockPreset.Create(Guid.NewGuid(), command.Name, command.WebsiteFilterType, command.PresetType, _clock.Now);

            foreach (var appName in command.BlockedApps)
                preset.AddBlockedApp(new BlockedApp(appName));

            foreach (var url in command.Websites)
                preset.AddWebsite(new Website(url));
        }
        catch (ArgumentException ex)
        {
            return CreateBlockPresetResult.Fail(ex.Message);
        }

        _presets.Add(preset);

        return CreateBlockPresetResult.Ok(preset.Id);
    }
}
