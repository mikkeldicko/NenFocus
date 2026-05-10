using FluentValidation;

namespace NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;

public sealed class UpdatePlannedBlockValidator : AbstractValidator<UpdatePlannedBlockCommand>
{
    public UpdatePlannedBlockValidator()
    {
        RuleFor(x => x.BlockId)
            .NotEmpty().WithMessage("Block ID is required.");

        RuleFor(x => x.ProfileId)
            .NotEmpty().WithMessage("Profile ID is required.");

        RuleFor(x => x.Start)
            .Must(start => start >= DateTime.UtcNow.AddMinutes(-5))
            .WithMessage("Start time cannot be more than 5 minutes in the past.");

        RuleFor(x => x.End)
            .GreaterThan(x => x.Start)
            .WithMessage("End time must be after start time.");
    }
}
