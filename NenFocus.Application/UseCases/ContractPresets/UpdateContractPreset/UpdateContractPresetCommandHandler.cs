using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.ContractPresets.UpdateContractPreset;

public sealed class UpdateContractPresetCommandHandler
    : ICommandHandler<UpdateContractPresetCommand, UpdateContractPresetResult>
{
    private readonly IContractPresetRepository _presets;

    public UpdateContractPresetCommandHandler(IContractPresetRepository presets)
    {
        _presets = presets;
    }

    public async Task<UpdateContractPresetResult> HandleAsync(
        UpdateContractPresetCommand command,
        CancellationToken cancellationToken = default)
    {
        var preset = await _presets.GetByIdAsync(command.PresetId, cancellationToken);
        if (preset is null)
            return UpdateContractPresetResult.Fail($"Preset '{command.PresetId}' not found.");

        if (await _presets.ExistsWithNameAsync(command.Name, excludeId: command.PresetId, cancellationToken: cancellationToken))
            return UpdateContractPresetResult.Fail($"A preset named '{command.Name}' already exists.");

        try
        {
            var workHours = command.WorkHoursStart is { } start && command.WorkHoursEnd is { } end
                ? new WorkHours(start, end)
                : null;

            preset.Update(
                command.Name,
                command.StrictnessRule,
                command.DailyFloorHours is { } floor ? new DailyFloor(floor) : null,
                workHours,
                command.AllowCancellation,
                command.RedemptionPlacementPolicy);
        }
        catch (ArgumentException ex)
        {
            return UpdateContractPresetResult.Fail(ex.Message);
        }

        await _presets.UpdateAsync(preset, cancellationToken);
        return UpdateContractPresetResult.Ok();
    }
}
