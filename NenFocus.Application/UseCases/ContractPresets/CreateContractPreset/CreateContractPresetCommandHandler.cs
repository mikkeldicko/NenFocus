using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.ContractPresets.CreateContractPreset;

public sealed class CreateContractPresetCommandHandler
    : ICommandHandler<CreateContractPresetCommand, CreateContractPresetResult>
{
    private readonly IContractPresetRepository _presets;

    public CreateContractPresetCommandHandler(IContractPresetRepository presets)
    {
        _presets = presets;
    }

    public async Task<CreateContractPresetResult> HandleAsync(
        CreateContractPresetCommand command,
        CancellationToken cancellationToken = default)
    {
        if (await _presets.ExistsWithNameAsync(command.Name, cancellationToken: cancellationToken))
            return CreateContractPresetResult.Fail($"A preset named '{command.Name}' already exists.");

        ContractPreset preset;
        try
        {
            var workHours = command.WorkHours;

            preset = ContractPreset.Create(
                Guid.NewGuid(),
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
            return CreateContractPresetResult.Fail(ex.Message);
        }

        await _presets.AddAsync(preset, cancellationToken);

        return CreateContractPresetResult.Ok(preset.Id);
    }
}