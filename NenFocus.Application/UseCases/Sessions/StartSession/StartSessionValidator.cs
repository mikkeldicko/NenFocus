using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Sessions.StartSession;

public sealed class StartSessionValidator : AbstractValidator<StartSessionCommand>
{
    public StartSessionValidator(IBlockProfileRepository profiles)
    {
        RuleFor(x => x.Goal).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ProfileId).NotEmpty().WithMessage("A block profile must be selected.");
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, 480);

        // Only impromptu sessions carry a ProfileId chosen by the user — planned sessions
        // derive the profile from the FocusBlock, which was already validated at scheduling time.
        When(x => x.ProfileId.HasValue && x.OriginatingBlockId is null, () =>
        {
            RuleFor(x => x.ProfileId!.Value)
                .MustAsync(async (id, ct) =>
                {
                    var profile = await profiles.GetByIdAsync(id, ct);
                    return profile is null || profile.ProfileType == BlockType.Focus;
                })
                .WithMessage("Only focus profiles can be used to start a session.");
        });

        When(x => x.BreakSettings != null && x.BreakSettings.Enabled, () =>
        {
            RuleFor(x => x.BreakSettings!.IntervalMinutes).Must(v => v is 25 or 45 or 60);
            RuleFor(x => x.BreakSettings!.BreakDurationMinutes).Must(v => v is 5 or 10 or 15);
        });
    }
}
