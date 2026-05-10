using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockProfiles.CreateBlockProfile;
using NenFocus.Domain.Constants;

namespace NenFocus.Application.Validators;

public sealed class CreateBlockProfileCommandValidator : AbstractValidator<CreateBlockProfileCommand>
{
    public CreateBlockProfileCommandValidator(IBlockProfileRepository profiles)
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Profile name is required.")
            .MaximumLength(60)
            .WithMessage("Profile name cannot exceed 60 characters.")
            .MustAsync(async (name, ct) =>
                !await profiles.ExistsWithNameAsync(name, cancellationToken: ct))
            .WithMessage("A profile with this name already exists.");

        RuleFor(x => x)
            .Must(x => x.BlockedApps.Count > 0 || x.Websites.Count > 0)
            .WithMessage("A profile must have at least one blocked app or website.");

        RuleForEach(x => x.BlockedApps)
            .NotEmpty()
            .WithMessage("App name cannot be empty.")
            .Must(app => !SystemSafetyConstants.UnkillableProcesses.Contains(Normalize(app)))
            .WithMessage(app => $"'{app}' is a critical system process and cannot be blocked.");

        RuleForEach(x => x.Websites)
            .NotEmpty()
            .WithMessage("Website URL cannot be empty.");

        RuleForEach(x => x.Tags)
            .MaximumLength(30)
            .WithMessage("Tags cannot exceed 30 characters each.");
    }

    private string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        try
        {
            // This handles "C:\Windows\explorer.exe" -> "explorer.exe"
            // and also just "explorer" -> "explorer"
            string fileName = System.IO.Path.GetFileName(input);

            if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName[..^4];
            }

            return fileName.Trim();
        }
        catch
        {
            return input.Trim();
        }
    }
}