using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;

public sealed class UpdatePlannedBlockCommandHandler : ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly ISessionRepository _sessions;

    public UpdatePlannedBlockCommandHandler(
        IPlannedBlockRepository blocks,
        IBlockPresetRepository presets,
        ISessionRepository sessions)
    {
        _blocks = blocks;
        _presets = presets;
        _sessions = sessions;
    }

    public async Task<UpdatePlannedBlockResult> HandleAsync(UpdatePlannedBlockCommand command, CancellationToken cancellationToken = default)
    {
        var startTime = command.Start.ToUniversalTime();
        var endTime = command.End.ToUniversalTime();

        var existing = await _blocks.GetByIdAsync(command.BlockId, cancellationToken);
        if (existing is null)
            return UpdatePlannedBlockResult.Fail($"Planned block '{command.BlockId}' not found.");

        var preset = await _presets.GetByIdAsync(command.PresetId, cancellationToken);
        if (preset is null)
            return UpdatePlannedBlockResult.Fail($"Preset '{command.PresetId}' not found.");

        if (existing is FocusBlock)
        {
            var overlapping = await _blocks.GetOverlappingAsync(
                startTime,
                endTime,
                ritualOnly: true,
                excludeId: command.BlockId,
                cancellationToken: cancellationToken);

            if (overlapping.Count > 0)
                return UpdatePlannedBlockResult.Fail("A Focus block already exists in this time window.");

            var activeSession = await _sessions.GetActiveAsync(cancellationToken);
            if (activeSession?.StartedAt is not null)
            {
                var sessionStart = activeSession.StartedAt.Value;
                var sessionEnd = sessionStart + activeSession.Duration.Value;
                if (sessionStart < endTime && sessionEnd > startTime)
                {
                    var localStart = sessionStart.ToLocalTime();
                    var localEnd = sessionEnd.ToLocalTime();
                    return UpdatePlannedBlockResult.Fail(
                        $"There is already an active focus session running from {localStart:HH:mm} to {localEnd:HH:mm} — please plan your session after {localEnd:HH:mm}.");
                }
            }
        }

        existing.Reschedule(startTime, endTime);
        existing.AssignPreset(command.PresetId);

        await _blocks.UpdateAsync(existing, cancellationToken);
        return UpdatePlannedBlockResult.Ok();
    }
}
