using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;

namespace NenFocus.Application.UseCases.Sessions.EndSession;

public sealed class EndSessionCommandHandler
    : ICommandHandler<EndSessionCommand, EndSessionResult>
{
    private readonly ISessionRepository _sessions;
    private readonly IBlockPresetRepository _presets;
    private readonly IPlannedBlockRepository _plannedBlocks;
    private readonly IBlockedAttemptRepository _attempts;
    private readonly ISystemClock _clock;

    public EndSessionCommandHandler(
        ISessionRepository sessions,
        IBlockPresetRepository presets,
        IPlannedBlockRepository plannedBlocks,
        IBlockedAttemptRepository attempts,
        ISystemClock clock)
    {
        _sessions = sessions;
        _presets = presets;
        _plannedBlocks = plannedBlocks;
        _attempts = attempts;
        _clock = clock;
    }

    public async Task<EndSessionResult> HandleAsync(
        EndSessionCommand command,
        CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(command.SessionId, ct);
        if (session is null)
            return EndSessionResult.Fail($"Session '{command.SessionId}' not found.");

        if (session.Status != SessionStatus.Active)
            return EndSessionResult.Fail("Session is not currently active.");

        var attempts = await _attempts.GetForSessionAsync(session.Id, ct);

        session.Complete(_clock.Now);
        // Capped at the planned end by Complete itself (Gap 63). Everything below reads the
        // recorded end, never "now", so an end noticed late can't leak past the plan.
        var endedAt = session.EndedAt!.Value;


        FocusBlock? originatingFocusBlock = null;
        if (session.OriginatingBlockId.HasValue)
        {
            var block = await _plannedBlocks.GetByIdAsync(session.OriginatingBlockId.Value, ct);
            if (block is FocusBlock focusBlock)
            {
                originatingFocusBlock = focusBlock;
                // Always, early or on time: the block's SessionEndedAt is a faithful copy of
                // Session.EndedAt, never a filtered one (Gap 86). Every reader compares it to
                // End itself, so an on-time end changes no answer.
                focusBlock.MarkSessionEnded(endedAt);
            }
        }

        var presetName = string.Empty;
        if (session.BlockPresetId != Guid.Empty)
        {
            var preset = await _presets.GetByIdAsync(session.BlockPresetId, ct);
            presetName = preset?.Name ?? string.Empty;
        }

        return EndSessionResult.Ok(PostSessionDto.From(session, originatingFocusBlock, presetName, attempts));
    }
}