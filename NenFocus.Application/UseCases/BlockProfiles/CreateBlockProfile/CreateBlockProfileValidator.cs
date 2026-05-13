using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Constants;
using NenFocus.Domain.Utils;

namespace NenFocus.Application.UseCases.BlockProfiles.CreateBlockProfile;

public sealed class CreateBlockProfileValidator : AbstractValidator<CreateBlockProfileCommand>
{
    public CreateBlockProfileValidator(IBlockProfileRepository profiles)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Profile name is required.")
            .WithMessage("A profile with this name already exists.");


        RuleFor(x => x)
            .Must(x => x.BlockedApps.Count > 0 || x.Websites.Count > 0)
            .WithMessage("A profile must have at least one blocked app or website.");

        RuleForEach(x => x.BlockedApps)
            .Must(app => !SystemSafetyConstants.UnkillableProcesses.Contains(ProcessUtils.NormalizeProcessName(app)))
            .WithMessage(app => $"'{app}' is a critical system process and cannot be blocked.");

        RuleForEach(x => x.Tags)
            .MaximumLength(30).WithMessage("Tags cannot exceed 30 characters.");
    }
}