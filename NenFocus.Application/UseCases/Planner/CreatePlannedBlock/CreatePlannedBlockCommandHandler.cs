using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed class CreatePlannedBlockCommandHandler : ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly ISessionRepository _sessions;
    private readonly ISystemClock _clock;
    private readonly IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> _computeRedemptionAttribution;

    public CreatePlannedBlockCommandHandler(
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

    public async Task<CreatePlannedBlockResult> HandleAsync(CreatePlannedBlockCommand command, CancellationToken cancellationToken = default)
    {
        var startTime = command.Start.ToUniversalTime();
        var endTime = command.End.ToUniversalTime();

        var preset = await _presets.GetByIdAsync(command.PresetId, cancellationToken);
        if (preset is null) return CreatePlannedBlockResult.Fail("Preset not found.");

        // Resolved before the overlap/active-session checks below, not just before
        // FocusBlock.Create — those checks must validate the window that will actually be
        // persisted (post-cap), not the longer one the caller originally asked for.
        Guid? contractId = command.ContractId;
        IReadOnlyList<RedemptionEntry> redemptionEntries = [];
        var redemptionCapped = false;

        if (command.BlockPresetType == BlockPresetType.Focus && !command.DeclineRedemption)
        {
            var attribution = await _computeRedemptionAttribution.HandleAsync(
                new ComputeRedemptionAttributionQuery(command.ContractId, command.PresetId, startTime, endTime, ExcludeBlockId: null),
                cancellationToken);
            contractId = attribution.ContractId;
            redemptionEntries = attribution.Entries
                .Select(e => new RedemptionEntry(e.MissedBlockId, e.MinutesCovered))
                .ToList();

            var cappedEnd = FocusBlock.CapEndToRedemptionCoverage(startTime, endTime, redemptionEntries);
            redemptionCapped = FocusBlock.WasRedemptionCoverageMeaningfullyCapped(endTime, cappedEnd);
            endTime = cappedEnd;
        }

        if (command.BlockPresetType == BlockPresetType.Focus)
        {
            var overlapping = await _blocks.GetOverlappingAsync(startTime, endTime, _clock.UtcNow, ritualOnly: true, cancellationToken: cancellationToken);
            if (overlapping.Count > 0) return CreatePlannedBlockResult.Fail("A Focus block already exists here.");

            var activeSession = await _sessions.GetActiveAsync(cancellationToken);
            if (activeSession?.StartedAt is not null)
            {
                var sessionStart = activeSession.StartedAt.Value;
                // EndAt, not StartedAt + Duration — for a Planned session, EndAt is pinned
                // to the originating block's own scheduled end (Session.Start), while
                // Duration is the block's full nominal length regardless of exactly when
                // the Ritual completed. A session started even slightly late (still within
                // the grace period) makes StartedAt + Duration overshoot the session's real
                // end by the lateness amount, wrongly rejecting a block that legitimately
                // fits right after it actually ends. Set together with StartedAt inside
                // Session.Start(), so it's never null here.
                var sessionEnd = activeSession.EndAt!.Value;
                if (sessionStart < endTime && sessionEnd > startTime)
                {
                    var localStart = sessionStart.ToLocalTime();
                    var localEnd = sessionEnd.ToLocalTime();
                    return CreatePlannedBlockResult.Fail(
                        $"There is already an active focus session running from {localStart:HH:mm} to {localEnd:HH:mm} — please plan your session after {localEnd:HH:mm}.");
                }
            }
        }

        PlannedBlock block;
        if (command.BlockPresetType == BlockPresetType.Focus)
        {
            block = FocusBlock.Create(Guid.NewGuid(), command.PresetId, startTime, endTime,
                contractId: contractId,
                redemptionEntries: redemptionEntries);
        }
        else
        {
            block = BackgroundBlock.Create(Guid.NewGuid(), command.PresetId, startTime, endTime,
                contractId: command.ContractId);
        }

        await _blocks.AddAsync(block, cancellationToken);
        return CreatePlannedBlockResult.Ok(block.Id, redemptionCapped);
    }
}
