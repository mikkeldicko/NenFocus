using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.Sessions.StartSession;

public sealed class StartSessionValidator : AbstractValidator<StartSessionCommand>
{
    public StartSessionValidator(IBlockPresetRepository presets)
    {
        RuleFor(x => x.Goal).NotEmpty().MaximumLength(500);
        RuleFor(x => x.PresetId).NotEmpty().WithMessage("A block preset must be selected.");
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, 480);

        // Only spontaneous sessions carry a PresetId chosen by the user — planned sessions
        // derive the preset from the FocusBlock, which was already validated at scheduling
        // time (and DeleteBlockPresetValidator refuses to delete a preset still assigned to
        // a future planned block, so that path can't go dangling later either).
        When(x => x.PresetId.HasValue && x.OriginatingBlockId is null, () =>
        {
            // Defense in depth: a stale/deleted PresetId must not silently start a session
            // with no real blocking behind it (BlockedApps/BlockedSites would resolve to
            // nothing) — the UI only ever offers presets that exist, but that's a
            // convenience, not a guarantee for whatever actually reaches this command.
            RuleFor(x => x.PresetId!.Value)
                .MustAsync(async (id, ct) => await presets.GetByIdAsync(id, ct) is not null)
                .WithMessage("The selected preset no longer exists — please pick another.");

            RuleFor(x => x.PresetId!.Value)
                .MustAsync(async (id, ct) =>
                {
                    var preset = await presets.GetByIdAsync(id, ct);
                    return preset is null || preset.PresetType == BlockPresetType.Focus;
                })
                .WithMessage("Only focus presets can be used to start a session.");
        });

        When(x => x.BreakSettings != null, () =>
        {
            RuleFor(x => x.BreakSettings!.IntervalMinutes).Must(v => v is 25 or 45 or 60);
            RuleFor(x => x.BreakSettings!.BreakDurationMinutes).Must(v => v is 5 or 10 or 15);
        });
    }
}
