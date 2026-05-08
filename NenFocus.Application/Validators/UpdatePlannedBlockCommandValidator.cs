using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Validators;

public sealed class UpdatePlannedBlockCommandValidator
    : AbstractValidator<UpdatePlannedBlockCommand>
{
    public UpdatePlannedBlockCommandValidator(IPlannedBlockRepository blocks)
    {
        RuleFor(x => x.BlockId)
            .NotEmpty()
            .WithMessage("Block ID is required.");

        RuleFor(x => x.ProfileId)
            .NotEmpty()
            .WithMessage("A block profile must be selected.");

        RuleFor(x => x.Start)
            .NotEmpty()
            .WithMessage("Start time is required.");

        RuleFor(x => x.End)
            .NotEmpty()
            .WithMessage("End time is required.")
            .GreaterThan(x => x.Start)
            .WithMessage("End time must be after start time.");

        RuleFor(x => x)
            .Must(x => (x.End - x.Start).TotalMinutes >= 5)
            .WithMessage("A block must be at least 5 minutes long.");

        // Re-check Focus+Focus overlap excluding this block
        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var existing = await blocks.GetByIdAsync(command.BlockId, ct);
                if (existing?.Type != BlockType.Focus) return true;

                var overlapping = await blocks.GetOverlappingAsync(
                    command.Start,
                    command.End,
                    BlockType.Focus,
                    excludeId: command.BlockId,
                    cancellationToken: ct);
                return overlapping.Count == 0;
            })
            .WithMessage("A Focus block already exists in this time window.");
    }
}
