using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BreakTimerPresets.DeleteBreakTimerPreset;

/// <summary>
/// No lock check, unlike DeleteBlockPresetCommandHandler — a BreakTimerPreset is
/// snapshotted by value into any Session using it, so nothing currently active or
/// historical depends on this row continuing to exist.
/// </summary>
public sealed class DeleteBreakTimerPresetCommandHandler
    : ICommandHandler<DeleteBreakTimerPresetCommand, DeleteBreakTimerPresetResult>
{
    private readonly IBreakTimerPresetRepository _presets;

    public DeleteBreakTimerPresetCommandHandler(IBreakTimerPresetRepository presets)
    {
        _presets = presets;
    }

    public async Task<DeleteBreakTimerPresetResult> HandleAsync(
        DeleteBreakTimerPresetCommand command,
        CancellationToken cancellationToken = default)
    {
        await _presets.DeleteAsync(command.PresetId, cancellationToken);
        return DeleteBreakTimerPresetResult.Ok();
    }
}
