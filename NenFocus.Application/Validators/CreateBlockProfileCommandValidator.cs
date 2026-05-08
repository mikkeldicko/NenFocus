using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockProfiles.CreateBlockProfile;

namespace NenFocus.Application.Validators;

public sealed class CreateBlockProfileCommandValidator
    : AbstractValidator<CreateBlockProfileCommand>
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
            .WithMessage("App name cannot be empty.");

        RuleForEach(x => x.Websites)
            .NotEmpty()
            .WithMessage("Website URL cannot be empty.");

        RuleForEach(x => x.Tags)
            .MaximumLength(30)
            .WithMessage("Tags cannot exceed 30 characters each.");
    }
}
