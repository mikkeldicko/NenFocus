using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.BlockProfiles.UpdateBlockProfile;

namespace NenFocus.Application.Validators;

public sealed class UpdateBlockProfileCommandValidator
    : AbstractValidator<UpdateBlockProfileCommand>
{
    public UpdateBlockProfileCommandValidator(IBlockProfileRepository profiles)
    {
        RuleFor(x => x.ProfileId)
            .NotEmpty()
            .WithMessage("Profile ID is required.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Profile name is required.")
            .MaximumLength(60)
            .WithMessage("Profile name cannot exceed 60 characters.")
            .MustAsync(async (command, name, ct) =>
                !await profiles.ExistsWithNameAsync(name, command.ProfileId, ct))
            .WithMessage("A profile with this name already exists.");

        RuleFor(x => x)
            .Must(x => x.BlockedApps.Count > 0 || x.Websites.Count > 0)
            .WithMessage("A profile must have at least one blocked app or website.");

        RuleForEach(x => x.Tags)
            .MaximumLength(30)
            .WithMessage("Tags cannot exceed 30 characters each.");
    }
}
