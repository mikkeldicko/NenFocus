using FluentValidation;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Sessions.StartSession;

public sealed class StartSessionValidator : AbstractValidator<StartSessionCommand>
{
    public StartSessionValidator(IBlockPresetRepository presets, IBreakTimerPresetRepository breakTimerPresets)
    {
        RuleFor(x => x.Goal).NotEmpty().MaximumLength(500);
        RuleFor(x => x.PresetId).NotEmpty().WithMessage("A block preset must be selected.");
        // A focus period lasts at least a minute and at most FocusBlock.MaxLength (Gap 89 Slice B, A1).
        // A planned session is its block's length — the block was held to the cap when it was saved —
        // so only a spontaneous one's chosen length is checked here.
        RuleFor(x => x.DurationMinutes).GreaterThanOrEqualTo(1);
        When(x => x.OriginatingBlockId is null, () =>
            RuleFor(x => x.DurationMinutes).LessThanOrEqualTo((int)FocusBlock.MaxLength.TotalMinutes));

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

        // A session's own extras are an app or a site like a preset's (Gap 112) — this used to let a
        // critical process through, stopped only by the enforcement service's own skip.
        RuleForEach(x => x.AdditionalBlockedApps)
            .Must(app => BlockedApp.Check(app) is null)
            .WithMessage((_, app) => BlockListEntryRefusalDto.ForApp(app, BlockedApp.Check(app)!.Value).Message);

        RuleForEach(x => x.AdditionalBlockedSites)
            .Must(site => Website.TryCreate(site, out _))
            .WithMessage((_, site) => BlockListEntryRefusalDto.ForWebsite(site).Message);

        // Same defense-in-depth reasoning as PresetId above — Hatsu only ever offers a
        // preset that both exists and fits the chosen duration, but that's a UI
        // convenience, not a guarantee for whatever actually reaches this command.
        When(x => x.BreakTimerPresetId.HasValue, () =>
        {
            RuleFor(x => x.BreakTimerPresetId!.Value)
                .MustAsync(async (id, ct) => await breakTimerPresets.GetByIdAsync(id, ct) is not null)
                .WithMessage("The selected break timer preset no longer exists — please pick another.");
        });
    }
}
