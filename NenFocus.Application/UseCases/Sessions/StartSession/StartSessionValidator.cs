using FluentValidation;
using NenFocus.Domain.Constants;
using System;
using System.IO;

namespace NenFocus.Application.UseCases.Sessions.StartSession;

public sealed class StartSessionValidator : AbstractValidator<StartSessionCommand>
{
    public StartSessionValidator()
    {
        RuleFor(x => x.Goal).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ProfileId).NotEmpty().WithMessage("A block profile must be selected.");
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, 480);

        // Safety Shield for Temporary Apps
        RuleForEach(x => x.TemporaryBlockedApps)
            .Must(app => !SystemSafetyConstants.UnkillableProcesses.Contains(Normalize(app)))
            .WithMessage(app => $"'{app}' is a critical system process and cannot be blocked.");

        When(x => x.BreakSettings != null && x.BreakSettings.Enabled, () =>
        {
            RuleFor(x => x.BreakSettings!.IntervalMinutes).Must(v => v is 25 or 45 or 60);
            RuleFor(x => x.BreakSettings!.BreakDurationMinutes).Must(v => v is 5 or 10 or 15);
        });
    }

    private string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        try
        {
            string fileName = Path.GetFileName(input);
            if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                fileName = fileName[..^4];
            return fileName.Trim();
        }
        catch { return input.Trim(); }
    }
}