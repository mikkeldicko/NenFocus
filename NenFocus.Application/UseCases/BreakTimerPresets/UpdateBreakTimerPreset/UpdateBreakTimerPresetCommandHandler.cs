using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.BreakTimerPresets.UpdateBreakTimerPreset;

public sealed class UpdateBreakTimerPresetCommandHandler
    : ICommandHandler<UpdateBreakTimerPresetCommand, UpdateBreakTimerPresetResult>
{
    private readonly IBreakTimerPresetRepository _presets;

    public UpdateBreakTimerPresetCommandHandler(IBreakTimerPresetRepository presets)
    {
        _presets = presets;
    }

    public async Task<UpdateBreakTimerPresetResult> HandleAsync(
        UpdateBreakTimerPresetCommand command,
        CancellationToken cancellationToken = default)
    {
        var preset = await _presets.GetByIdAsync(command.PresetId, cancellationToken);
        if (preset is null)
            return UpdateBreakTimerPresetResult.Fail($"Preset '{command.PresetId}' not found.");

        if (await _presets.ExistsWithNameAsync(command.Name, excludeId: command.PresetId, cancellationToken: cancellationToken))
            return UpdateBreakTimerPresetResult.Fail($"A preset named '{command.Name}' already exists.");

        try
        {
            var breakTimer = new BreakTimer(
                command.WorkMinutes,
                command.ShortBreakMinutes,
                command.CyclesBeforeLongBreak,
                command.LongBreakMinutes);
            preset.Update(command.Name, breakTimer);
        }
        catch (ArgumentException ex)
        {
            return UpdateBreakTimerPresetResult.Fail(ex.Message);
        }

        await _presets.UpdateAsync(preset, cancellationToken);
        return UpdateBreakTimerPresetResult.Ok();
    }
}
