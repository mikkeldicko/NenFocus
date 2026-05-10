using FluentValidation;

namespace NenFocus.Application.UseCases.Planner.DeletePlannedBlock;

public sealed class DeletePlannedBlockValidator : AbstractValidator<DeletePlannedBlockCommand>
{
    public DeletePlannedBlockValidator()
    {
        RuleFor(x => x.BlockId)
            .NotEmpty().WithMessage("Block ID is required.");
    }
}
