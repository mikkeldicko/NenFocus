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
            var workHours = command.WorkHours;

            preset.Update(
                command.Name,
                new ContractRules(
                    command.StrictnessRule,
                    command.DailyFloorHours is { } floor ? new DailyFloor(floor) : null,
                    command.AllowCancellation,
                    command.KeepBlockingDuringBreaks,
                    command.RedemptionPlacementPolicy,
                    command.BonusBlocksFollowStrictness),
                workHours);
        }
        catch (ArgumentException ex)
        {
            return UpdateContractPresetResult.Fail(ex.Message);
        }

        return UpdateContractPresetResult.Ok();
    }
}
