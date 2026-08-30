using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;

public sealed class UpdatePlannedBlockCommandHandler : ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly ISessionRepository _sessions;
    private readonly ISystemClock _clock;
    private readonly IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> _computeRedemptionAttribution;

    public UpdatePlannedBlockCommandHandler(
        IPlannedBlockRepository blocks,
        IBlockPresetRepository presets,
        ISessionRepository sessions,
        ISystemClock clock,
        IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> computeRedemptionAttribution)
    {
        _blocks = blocks;
        _presets = presets;
        _sessions = sessions;
        _clock = clock;
        _computeRedemptionAttribution = computeRedemptionAttribution;
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

        IReadOnlyList<RedemptionEntry> redemptionEntries = [];
        var redemptionCapped = false;

        if (existing is FocusBlock existingFocusBlock)
        {
            // Recomputed fresh on every edit, scoped only to the block's own already-known
            // contract — never re-parented to a different one via an ordinary edit (see
            // UpdatePlannedBlockCommand). A standalone block (ContractId null) stays
            // standalone; editing it never auto-discovers a contract for the first time.
            // Resolved before the overlap/active-session checks below, not just before
            // Reschedule — those checks must validate the window that will actually be
            // persisted (post-cap), not the longer one the caller originally asked for.
            if (existingFocusBlock.ContractId is { } contractId)
            {
                var attribution = await _computeRedemptionAttribution.HandleAsync(
                    new ComputeRedemptionAttributionQuery(contractId, command.PresetId, startTime, endTime, existing.Id),
                    cancellationToken);
                redemptionEntries = attribution.Entries
                    .Select(e => new RedemptionEntry(e.MissedBlockId, e.MinutesCovered))
                    .ToList();

                var cappedEnd = FocusBlock.CapEndToRedemptionCoverage(startTime, endTime, redemptionEntries);
                redemptionCapped = cappedEnd != endTime;
                endTime = cappedEnd;
            }

            var overlapping = await _blocks.GetOverlappingAsync(
                startTime,
                endTime,
                _clock.UtcNow,
                ritualOnly: true,
                excludeId: command.BlockId,
                cancellationToken: cancellationToken);

            if (overlapping.Count > 0)
                return UpdatePlannedBlockResult.Fail("A Focus block already exists in this time window.");

            var activeSession = await _sessions.GetActiveAsync(cancellationToken);
            if (activeSession?.StartedAt is not null)
            {
                var sessionStart = activeSession.StartedAt.Value;
                // EndAt, not StartedAt + Duration — see CreatePlannedBlockCommandHandler's
                // identical check for the full explanation. Set together with StartedAt
                // inside Session.Start(), so it's never null here.
                var sessionEnd = activeSession.EndAt!.Value;
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

        if (existing is FocusBlock focusBlockToUpdate)
            focusBlockToUpdate.AssignRedemptionEntries(redemptionEntries);

        await _blocks.UpdateAsync(existing, cancellationToken);
        return UpdatePlannedBlockResult.Ok(redemptionCapped);
    }
}
