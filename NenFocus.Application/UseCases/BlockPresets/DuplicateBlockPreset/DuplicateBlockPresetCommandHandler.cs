using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.UseCases.BlockPresets.DuplicateBlockPreset;

public sealed class DuplicateBlockPresetCommandHandler
    : ICommandHandler<DuplicateBlockPresetCommand, DuplicateBlockPresetResult>
{
    private readonly IBlockPresetRepository _presets;
    private readonly ISystemClock _clock;

    public DuplicateBlockPresetCommandHandler(IBlockPresetRepository presets, ISystemClock clock)
    {
        _presets = presets;
        _clock = clock;
    }

    public async Task<DuplicateBlockPresetResult> HandleAsync(
        DuplicateBlockPresetCommand command,
        CancellationToken cancellationToken = default)
    {
        var source = await _presets.GetByIdAsync(command.SourcePresetId, cancellationToken);
        if (source is null)
            return DuplicateBlockPresetResult.Fail($"Preset '{command.SourcePresetId}' not found.");

        var baseName = command.NewName ?? $"Copy of {source.Name}";

        // Sequential numbering, same convention as Explorer/Finder/Google Docs — checked
        // and incremented one at a time rather than a single existence check, since a
        // timestamp suffix can't actually guarantee uniqueness (two duplicates within the
        // same minute would collide) and this loop always finds a genuinely free name.
        var newName = baseName;
        var suffix = 2;
        while (await _presets.ExistsWithNameAsync(newName, source.PresetType, cancellationToken: cancellationToken))
        {
            newName = $"{baseName} ({suffix})";
            suffix++;
        }

        BlockPreset duplicate;
        try
        {
            duplicate = source.Duplicate(newName, Guid.NewGuid(), _clock.Now);
        }
        catch (ArgumentException ex)
        {
            return DuplicateBlockPresetResult.Fail(ex.Message);
        }

        _presets.Add(duplicate);

        return DuplicateBlockPresetResult.Ok(duplicate.Id);
    }
}
