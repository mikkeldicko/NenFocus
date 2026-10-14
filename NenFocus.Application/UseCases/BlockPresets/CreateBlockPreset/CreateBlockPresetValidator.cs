using FluentValidation;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.BlockPresets.CreateBlockPreset;

public sealed class CreateBlockPresetValidator : AbstractValidator<CreateBlockPresetCommand>
{
    public CreateBlockPresetValidator(IBlockPresetRepository presets)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Preset name is required.");

        RuleFor(x => x)
            .Must(x => x.BlockedApps.Count > 0 || x.Websites.Count > 0)
            .WithMessage("A preset must have at least one blocked app or website.");

        // BlockedApp and Website say what can go on the list (Gap 112); worded in BlockListEntryRefusalDto.
        RuleForEach(x => x.BlockedApps)
            .Must(app => BlockedApp.Check(app) is null)
            .WithMessage((_, app) => BlockListEntryRefusalDto.ForApp(app, BlockedApp.Check(app)!.Value).Message);

        RuleForEach(x => x.Websites)
            .Must(site => Website.TryCreate(site, out _))
            .WithMessage((_, site) => BlockListEntryRefusalDto.ForWebsite(site).Message);
    }
}