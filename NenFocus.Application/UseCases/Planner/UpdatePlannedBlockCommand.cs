using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;

public sealed record UpdatePlannedBlockCommand
{
    public Guid BlockId { get; init; }
    public Guid ProfileId { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
}

public sealed record UpdatePlannedBlockResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static UpdatePlannedBlockResult Ok() => new() { Success = true };
    public static UpdatePlannedBlockResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}

public sealed class UpdatePlannedBlockCommandHandler
    : ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockProfileRepository _profiles;

    public UpdatePlannedBlockCommandHandler(
        IPlannedBlockRepository blocks,
        IBlockProfileRepository profiles)
    {
        _blocks = blocks;
        _profiles = profiles;
    }

    public async Task<UpdatePlannedBlockResult> HandleAsync(
        UpdatePlannedBlockCommand command,
        CancellationToken cancellationToken = default)
    {
        var existing = await _blocks.GetByIdAsync(command.BlockId, cancellationToken);
        if (existing is null)
            return UpdatePlannedBlockResult.Fail($"Planned block '{command.BlockId}' not found.");

        var profile = await _profiles.GetByIdAsync(command.ProfileId, cancellationToken);
        if (profile is null)
            return UpdatePlannedBlockResult.Fail($"Profile '{command.ProfileId}' not found.");

        // Re-check Focus+Focus overlap excluding this block
        if (existing.Type == BlockType.Focus)
        {
            var overlapping = await _blocks.GetOverlappingAsync(
                command.Start,
                command.End,
                BlockType.Focus,
                excludeId: command.BlockId,
                cancellationToken: cancellationToken);

            if (overlapping.Count > 0)
                return UpdatePlannedBlockResult.Fail(
                    "A Focus block already exists in this time window.");
        }

        // Delete and recreate with same Id for now (no Update method on derived types)
        await _blocks.DeleteAsync(command.BlockId, cancellationToken);

        PlannedBlock updated;
        if (existing.Type == BlockType.Focus)
        {
            updated = FocusBlock.Create(
                sessionId: Guid.Empty,
                blockProfileId: command.ProfileId,
                type: existing.Type,
                start: command.Start,
                end: command.End,
                isPlanned: true);
        }
        else
        {
            updated = PassiveBlock.Create(
                blockProfileId: command.ProfileId,
                start: command.Start,
                end: command.End);
        }

        await _blocks.AddAsync(updated, cancellationToken);

        return UpdatePlannedBlockResult.Ok();
    }
}