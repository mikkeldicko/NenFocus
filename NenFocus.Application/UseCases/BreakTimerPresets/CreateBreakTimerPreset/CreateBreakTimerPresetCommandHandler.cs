using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.BreakTimerPresets.CreateBreakTimerPreset;

public sealed class CreateBreakTimerPresetCommandHandler
    : ICommandHandler<CreateBreakTimerPresetCommand, CreateBreakTimerPresetResult>
{
    private readonly IBreakTimerPresetRepository _presets;

    public CreateBreakTimerPresetCommandHandler(IBreakTimerPresetRepository presets)
    {
        _presets = presets;
    }

    public async Task<CreateBreakTimerPresetResult> HandleAsync(
        CreateBreakTimerPresetCommand command,
        CancellationToken cancellationToken = default)
    {
        if (await _presets.ExistsWithNameAsync(command.Name, cancellationToken: cancellationToken))
            return CreateBreakTimerPresetResult.Fail($"A preset named '{command.Name}' already exists.");

        BreakTimerPreset preset;
        try
        {
            var breakTimer = new BreakTimer(
                command.WorkMinutes,
                command.ShortBreakMinutes,
                command.CyclesBeforeLongBreak,
                command.LongBreakMinutes);
            preset = BreakTimerPreset.Create(Guid.NewGuid(), command.Name, breakTimer);
        }
        catch (ArgumentException ex)
        {
            return CreateBreakTimerPresetResult.Fail(ex.Message);
        }

        _presets.Add(preset);

        return CreateBreakTimerPresetResult.Ok(preset.Id);
    }
}
