using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.UseCases.BlockPresets.DuplicateBlockPreset;

public sealed class DuplicateBlockPresetCommandHandler
    : ICommandHandler<DuplicateBlockPresetCommand, DuplicateBlockPresetResult>
{
    private readonly IBlockPresetRepository _presets;

    public DuplicateBlockPresetCommandHandler(IBlockPresetRepository presets)
        => _presets = presets;

    public async Task<DuplicateBlockPresetResult> HandleAsync(
        DuplicateBlockPresetCommand command,
        CancellationToken cancellationToken = default)
    {
        var source = await _presets.GetByIdAsync(command.SourcePresetId, cancellationToken);
        if (source is null)
            return DuplicateBlockPresetResult.Fail($"Preset '{command.SourcePresetId}' not found.");

        var newName = command.NewName ?? $"Copy of {source.Name}";

        var now = DateTime.UtcNow;
        if (await _presets.ExistsWithNameAsync(newName, cancellationToken: cancellationToken))
            newName = $"{newName} ({now:HH:mm})";

        BlockPreset duplicate;
        try
        {
            duplicate = source.Duplicate(newName, Guid.NewGuid(), now);
        }
        catch (ArgumentException ex)
        {
            return DuplicateBlockPresetResult.Fail(ex.Message);
        }

        await _presets.AddAsync(duplicate, cancellationToken);

        return DuplicateBlockPresetResult.Ok(duplicate.Id);
    }
}
