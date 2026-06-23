using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;

public sealed class UpdatePlannedBlockCommandHandler : ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockProfileRepository _profiles;
    private readonly IValidator<UpdatePlannedBlockCommand> _validator;

    public UpdatePlannedBlockCommandHandler(
        IPlannedBlockRepository blocks,
        IBlockProfileRepository profiles,
        IValidator<UpdatePlannedBlockCommand> validator)
    {
        _blocks = blocks;
        _profiles = profiles;
        _validator = validator;
    }

    public async Task<UpdatePlannedBlockResult> HandleAsync(UpdatePlannedBlockCommand command, CancellationToken cancellationToken = default)
    {
        var validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return UpdatePlannedBlockResult.Fail(validationResult.Errors.First().ErrorMessage);

        var startTime = command.Start.ToUniversalTime();
        var endTime = command.End.ToUniversalTime();

        var existing = await _blocks.GetByIdAsync(command.BlockId, cancellationToken);
        if (existing is null)
            return UpdatePlannedBlockResult.Fail($"Planned block '{command.BlockId}' not found.");

        var profile = await _profiles.GetByIdAsync(command.ProfileId, cancellationToken);
        if (profile is null)
            return UpdatePlannedBlockResult.Fail($"Profile '{command.ProfileId}' not found.");

        if (existing is RitualBlock)
        {
            var overlapping = await _blocks.GetOverlappingAsync(
                startTime,
                endTime,
                ritualOnly: true,
                excludeId: command.BlockId,
                cancellationToken: cancellationToken);

            if (overlapping.Count > 0)
                return UpdatePlannedBlockResult.Fail("A Ritual block already exists in this time window.");
        }

        existing.Reschedule(startTime, endTime);
        existing.AssignProfile(command.ProfileId);

        await _blocks.UpdateAsync(existing, cancellationToken);
        return UpdatePlannedBlockResult.Ok();
    }
}
