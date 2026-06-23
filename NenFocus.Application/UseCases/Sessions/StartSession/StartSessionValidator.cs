using FluentValidation;
using NenFocus.Domain.Constants;

namespace NenFocus.Application.UseCases.Sessions.StartSession;

public sealed class StartSessionValidator : AbstractValidator<StartSessionCommand>
{
    public StartSessionValidator()
    {
        RuleFor(x => x.Goal).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ProfileId).NotEmpty().WithMessage("A block profile must be selected.");
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, 480);

        RuleForEach(x => x.TemporaryBlockedApps)
            .Must(app => !SystemSafetyConstants.UnkillableProcesses.Contains(ProcessUtils.NormalizeProcessName(app)))
            .WithMessage(app => $"'{app}' is a critical system process and cannot be blocked.");

        When(x => x.BreakSettings != null && x.BreakSettings.Enabled, () =>
        {
            RuleFor(x => x.BreakSettings!.IntervalMinutes).Must(v => v is 25 or 45 or 60);
            RuleFor(x => x.BreakSettings!.BreakDurationMinutes).Must(v => v is 5 or 10 or 15);
        });
    }
}
