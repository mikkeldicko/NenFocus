using FluentValidation;
using NenFocus.Domain.Constants;
using System;
using System.IO;

namespace NenFocus.Application.UseCases.BlockProfiles.UpdateBlockProfile;

public sealed class UpdateBlockProfileValidator : AbstractValidator<UpdateBlockProfileCommand>
{
    public UpdateBlockProfileValidator()
    {
        RuleForEach(x => x.BlockedApps)
     .Must(app => !SystemSafetyConstants.UnkillableProcesses.Contains(Normalize(app)))
     // (cmd, app) ensures 'app' refers to the specific string in the list, 
     // not the whole UpdateBlockProfileCommand object.
     .WithMessage((cmd, app) => $"'{app}' is a critical system process and cannot be blocked.");
    }

    private string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        try
        {
            // Path.GetFileName handles C:\Windows\explorer.exe -> explorer.exe
            string fileName = Path.GetFileName(input);

            if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                fileName = fileName[..^4];

            // Return lowercase to match the HashSet in SystemSafetyConstants
            return fileName.Trim().ToLowerInvariant();
        }
        catch { return input.Trim().ToLowerInvariant(); }
    }
}