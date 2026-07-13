using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed class CreatePlannedBlockCommandHandler : ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly ISessionRepository _sessions;

    public CreatePlannedBlockCommandHandler(
        IPlannedBlockRepository blocks,
        IBlockPresetRepository presets,
        ISessionRepository sessions)
    {
        _blocks = blocks;
        _presets = presets;
        _sessions = sessions;
    }

    public async Task<CreatePlannedBlockResult> HandleAsync(CreatePlannedBlockCommand command, CancellationToken cancellationToken = default)
    {
        var startTime = command.Start.ToUniversalTime();
        var endTime = command.End.ToUniversalTime();

        var preset = await _presets.GetByIdAsync(command.PresetId, cancellationToken);
        if (preset is null) return CreatePlannedBlockResult.Fail("Preset not found.");

        if (command.BlockPresetType == BlockPresetType.Focus)
        {
            var overlapping = await _blocks.GetOverlappingAsync(startTime, endTime, ritualOnly: true, cancellationToken: cancellationToken);
            if (overlapping.Count > 0) return CreatePlannedBlockResult.Fail("A Focus block already exists here.");

            var activeSession = await _sessions.GetActiveAsync(cancellationToken);
            if (activeSession?.StartedAt is not null)
            {
                var sessionStart = activeSession.StartedAt.Value;
                var sessionEnd = sessionStart + activeSession.Duration.Value;
                if (sessionStart < endTime && sessionEnd > startTime)
                {
                    var localStart = sessionStart.ToLocalTime();
                    var localEnd = sessionEnd.ToLocalTime();
                    return CreatePlannedBlockResult.Fail(
                        $"There is already an active focus session running from {localStart:HH:mm} to {localEnd:HH:mm} — please plan your session after {localEnd:HH:mm}.");
                }
            }
        }

        PlannedBlock block = command.BlockPresetType == BlockPresetType.Focus
            ? FocusBlock.Create(Guid.NewGuid(), command.PresetId, startTime, endTime,
                contractId: command.ContractId,
                makeUpForEntries: command.MakeUpForEntries
                    .Select(e => new MakeUpForEntry(e.MissedBlockId, e.MinutesCovered))
                    .ToList())
            : BackgroundBlock.Create(Guid.NewGuid(), command.PresetId, startTime, endTime,
                contractId: command.ContractId);

        await _blocks.AddAsync(block, cancellationToken);
        return CreatePlannedBlockResult.Ok(block.Id);
    }
}
