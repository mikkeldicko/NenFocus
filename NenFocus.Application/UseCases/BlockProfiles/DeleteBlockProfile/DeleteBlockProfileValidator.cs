using FluentValidation;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BlockProfiles.DeleteBlockProfile;

public sealed class DeleteBlockProfileValidator : AbstractValidator<DeleteBlockProfileCommand>
{
    public DeleteBlockProfileValidator(
        ISessionRepository sessions,
        IPlannedBlockRepository plannedBlocks,
        ISystemClock clock)
    {
        RuleFor(x => x.ProfileId)
            .MustAsync(async (id, ct) =>
            {
                var active = await sessions.GetActiveAsync(ct);
                return active?.BlockProfileId != id;
            })
            .WithMessage("This profile is in use by an active session and cannot be deleted.");

        RuleFor(x => x.ProfileId)
            .MustAsync(async (id, ct) =>
                !await plannedBlocks.HasFutureBlocksForProfileAsync(id, clock.UtcNow, ct))
            .WithMessage("This profile is assigned to one or more future planned blocks and cannot be deleted.");
    }
}
