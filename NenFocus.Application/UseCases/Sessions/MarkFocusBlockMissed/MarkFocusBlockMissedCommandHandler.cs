using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Sessions.MarkFocusBlockMissed;

public sealed class MarkFocusBlockMissedCommandHandler
    : ICommandHandler<MarkFocusBlockMissedCommand, MarkFocusBlockMissedResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISessionRepository _sessions;
    private readonly IBlockPresetRepository _presets;

    public MarkFocusBlockMissedCommandHandler(
        IPlannedBlockRepository blocks,
        ISessionRepository sessions,
        IBlockPresetRepository presets)
    {
        _blocks = blocks;
        _sessions = sessions;
        _presets = presets;
    }

    public async Task<MarkFocusBlockMissedResult> HandleAsync(
        MarkFocusBlockMissedCommand command,
        CancellationToken ct = default)
    {
        var block = await _blocks.GetByIdAsync(command.FocusBlockId, ct) as FocusBlock;
        if (block is null)
            return MarkFocusBlockMissedResult.Fail("Focus block not found.");

        var session = Session.Create(
            id: Guid.NewGuid(),
            goal: new SessionGoal("(missed — no goal set)"),
            duration: new SessionDuration(block.GetDuration()),
            blockPresetId: block.BlockPresetId,
            origin: SessionOrigin.Planned,
            originatingBlockId: block.Id);

        session.Miss(block.Scheduled);

        _sessions.Add(session);

        var presetName = string.Empty;
        if (block.BlockPresetId != Guid.Empty)
        {
            var preset = await _presets.GetByIdAsync(block.BlockPresetId, ct);
            presetName = preset?.Name ?? string.Empty;
        }

        // A missed session tracked no attempts: nothing ran to attempt anything against.
        return MarkFocusBlockMissedResult.Ok(session.Id, PostSessionDto.From(session, block, presetName, []));
    }
}
