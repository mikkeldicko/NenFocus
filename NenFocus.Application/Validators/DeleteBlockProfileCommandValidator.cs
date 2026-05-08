using FluentValidation;
using NenFocus.Application.UseCases.BlockProfiles.DeleteBlockProfile;

namespace NenFocus.Application.Validators;

public sealed class DeleteBlockProfileCommandValidator
    : AbstractValidator<DeleteBlockProfileCommand>
{
    public DeleteBlockProfileCommandValidator()
    {
        RuleFor(x => x.ProfileId)
            .NotEmpty()
            .WithMessage("Profile ID is required.");
    }
}
