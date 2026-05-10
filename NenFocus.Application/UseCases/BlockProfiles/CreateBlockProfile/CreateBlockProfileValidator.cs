using FluentValidation;
using NenFocus.Domain.Constants;
using System;
using System.IO;

namespace NenFocus.Application.UseCases.BlockProfiles.CreateBlockProfile;

public sealed class CreateBlockProfileValidator : AbstractValidator<CreateBlockProfileCommand>
{
    public CreateBlockProfileValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Profile name is required.")
            .MaximumLength(60).WithMessage("Profile name cannot exceed 60 characters.");

        RuleForEach(x => x.BlockedApps)
            .NotEmpty()
            .Must(app => !SystemSafetyConstants.UnkillableProcesses.Contains(Normalize(app)))
            .WithMessage(app => $"'{app}' is a critical system process and cannot be blocked.");
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
        catch
        {
            return input.Trim();
        }
    }
}