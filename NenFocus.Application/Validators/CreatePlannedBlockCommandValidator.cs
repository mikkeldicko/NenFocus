using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.Planner.CreatePlannedBlock;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Validators;

public sealed class CreatePlannedBlockCommandValidator
    : AbstractValidator<CreatePlannedBlockCommand>
{
    public CreatePlannedBlockCommandValidator(IPlannedBlockRepository blocks)
    {
        RuleFor(x => x.ProfileId)
            .NotEmpty()
            .WithMessage("A block profile must be selected.");

        RuleFor(x => x.Start)
            .NotEmpty()
            .WithMessage("Start time is required.")
            .GreaterThanOrEqualTo(DateTime.UtcNow.Date)
            .WithMessage("Cannot schedule a block in the past.");

        RuleFor(x => x.End)
            .NotEmpty()
            .WithMessage("End time is required.")
            .GreaterThan(x => x.Start)
            .WithMessage("End time must be after start time.");

        RuleFor(x => x)
            .Must(x => (x.End - x.Start).TotalMinutes >= 5)
            .WithMessage("A block must be at least 5 minutes long.");

        // Focus + Focus overlap rule — async check against repository
        When(x => x.BlockType == BlockType.Focus, () =>
        {
            RuleFor(x => x)
                .MustAsync(async (command, ct) =>
                {
                    var overlapping = await blocks.GetOverlappingAsync(
                        command.Start,
                        command.End,
                        BlockType.Focus,
                        cancellationToken: ct);
                    return overlapping.Count == 0;
                })
                .WithMessage("A Focus block already exists in this time window.");
        });
    }
}
