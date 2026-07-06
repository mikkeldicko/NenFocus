using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed class CreatePlannedBlockValidator : AbstractValidator<CreatePlannedBlockCommand>
{
    public CreatePlannedBlockValidator(IBlockPresetRepository presets, ISystemClock clock)
    {
        RuleFor(x => x.PresetId)
            .NotEmpty().WithMessage("Preset ID is required.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.PresetId == Guid.Empty) return true;
                var preset = await presets.GetByIdAsync(command.PresetId, ct);
                return preset is null || preset.PresetType == command.BlockPresetType;
            })
            .WithMessage(x =>
                $"A {x.BlockPresetType.ToString().ToLowerInvariant()} block requires a {x.BlockPresetType.ToString().ToLowerInvariant()} preset.");

        RuleFor(x => x.Start)
            .Must(start => start > clock.UtcNow)
            .WithMessage("Start time must be in the future.");

        RuleFor(x => x.End)
            .GreaterThan(x => x.Start)
            .WithMessage("End time must be after start time.")
            .Must((command, end) => (end - command.Start).TotalMinutes >= 1)
            .WithMessage("Block must be at least 1 minute long.");
    }
}
