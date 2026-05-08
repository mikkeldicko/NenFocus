using FluentValidation;
using NenFocus.Application.UseCases.Sessions.StartSession;

namespace NenFocus.Application.Validators;

public sealed class StartSessionCommandValidator
    : AbstractValidator<StartSessionCommand>
{
    public StartSessionCommandValidator()
    {
        RuleFor(x => x.Goal)
            .NotEmpty()
            .WithMessage("A session goal is required.")
            .MaximumLength(500)
            .WithMessage("Goal cannot exceed 500 characters.");

        RuleFor(x => x.ProfileId)
            .NotEmpty()
            .WithMessage("A block profile must be selected.");

        RuleFor(x => x.DurationMinutes)
            .GreaterThan(0)
            .WithMessage("Duration must be at least 1 minute.")
            .LessThanOrEqualTo(480)
            .WithMessage("Duration cannot exceed 8 hours.");

        // Validate break settings only when reminders are enabled
        When(x => x.BreakSettings != null && x.BreakSettings.Enabled, () =>
        {
            RuleFor(x => x.BreakSettings!.IntervalMinutes)
                .Must(v => v is 25 or 45 or 60)
                .WithMessage("Break interval must be 25, 45, or 60 minutes.");

            RuleFor(x => x.BreakSettings!.BreakDurationMinutes)
                .Must(v => v is 5 or 10 or 15)
                .WithMessage("Break duration must be 5, 10, or 15 minutes.");

            RuleFor(x => x.BreakSettings!.IntervalMinutes)
                .LessThan(x => x.DurationMinutes)
                .WithMessage("Break interval cannot be longer than the session duration.");
        });
    }
}
