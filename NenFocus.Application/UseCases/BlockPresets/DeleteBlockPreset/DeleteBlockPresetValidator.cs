using FluentValidation;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BlockPresets.DeleteBlockPreset;

public sealed class DeleteBlockPresetValidator : AbstractValidator<DeleteBlockPresetCommand>
{
    public DeleteBlockPresetValidator(
        ISessionRepository sessions,
        IPlannedBlockRepository plannedBlocks,
        ISystemClock clock)
    {
        RuleFor(x => x.PresetId)
            .MustAsync(async (id, ct) =>
            {
                var active = await sessions.GetActiveAsync(ct);
                return active?.BlockPresetId != id;
            })
            .WithMessage("This preset is in use by an active session and cannot be deleted.");

        RuleFor(x => x.PresetId)
            .MustAsync(async (id, ct) =>
                !await plannedBlocks.HasFutureBlocksForPresetAsync(id, clock.Now, ct))
            .WithMessage("This preset is assigned to one or more future planned blocks and cannot be deleted.");
    }
}