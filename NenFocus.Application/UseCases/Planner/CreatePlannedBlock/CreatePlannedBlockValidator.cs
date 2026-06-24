using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed class CreatePlannedBlockValidator : AbstractValidator<CreatePlannedBlockCommand>
{
    public CreatePlannedBlockValidator(IBlockProfileRepository profiles, ISystemClock clock)
    {
        RuleFor(x => x.ProfileId)
            .NotEmpty().WithMessage("Profile ID is required.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.ProfileId == Guid.Empty) return true;
                var profile = await profiles.GetByIdAsync(command.ProfileId, ct);
                return profile is null || profile.ProfileType == command.BlockType;
            })
            .WithMessage(x =>
                $"A {x.BlockType.ToString().ToLowerInvariant()} block requires a {x.BlockType.ToString().ToLowerInvariant()} profile.");

        RuleFor(x => x.Start)
            .Must(start => start > clock.UtcNow)
            .WithMessage("Start time must be in the future.");

        RuleFor(x => x.End)
            .GreaterThan(x => x.Start)
            .WithMessage("End time must be after start time.");
    }
}
