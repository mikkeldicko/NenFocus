using FluentValidation;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.BlockPresets.UpdateBlockPreset;

public sealed class UpdateBlockPresetValidator : AbstractValidator<UpdateBlockPresetCommand>
{
    public UpdateBlockPresetValidator(
        ISessionRepository sessions,
        IPlannedBlockRepository plannedBlocks,
        IBlockPresetRepository presets,
        ISystemClock clock)
    {
        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var lockedByActiveSession = (await sessions.GetActiveAsync(ct))?.BlockPresetId == command.PresetId;
                var lockedByFutureBlock = await plannedBlocks.HasFutureBlocksForPresetAsync(command.PresetId, clock.Now, ct);

                if (!lockedByActiveSession && !lockedByFutureBlock)
                    return true;

                var current = await presets.GetByIdAsync(command.PresetId, ct);
                return current is null || IsAddOnlyChange(command, current);
            })
            .WithMessage("This preset is in use by an active session or a scheduled block (planned directly or under a Nen Contract) — while locked, you may only add apps or websites. Removing entries, renaming, or changing its type or filter is not allowed until it's free.");

        // BlockedApp and Website say what can go on the list (Gap 112); worded in BlockListEntryRefusalDto.
        RuleForEach(x => x.BlockedApps)
            .Must(app => BlockedApp.Check(app) is null)
            .WithMessage((_, app) => BlockListEntryRefusalDto.ForApp(app, BlockedApp.Check(app)!.Value).Message);

        RuleForEach(x => x.Websites)
            .Must(site => Website.TryCreate(site, out _))
            .WithMessage((_, site) => BlockListEntryRefusalDto.ForWebsite(site).Message);
    }

    /// <summary>
    /// True only if every field but BlockedApps/Websites is unchanged from the currently
    /// persisted preset, and the proposed lists keep it at least as strict — the strictness
    /// direction itself (which depends on WebsiteFilterType) is BlockPreset's own rule, not
    /// re-derived here.
    /// </summary>
    private static bool IsAddOnlyChange(UpdateBlockPresetCommand command, BlockPreset current)
    {
        if (command.Name.Trim() != current.Name) return false;
        if (command.WebsiteFilterType != current.WebsiteFilterType) return false;
        if (command.PresetType != current.PresetType) return false;

        // Entries that aren't an app or a site are refused by the per-entry rules anyway, so leaving
        // them out here never decides the outcome.
        var newApps = command.BlockedApps.Where(a => BlockedApp.Check(a) is null).Select(a => new BlockedApp(a)).ToHashSet();
        var newSites = new HashSet<Website>();
        foreach (var url in command.Websites)
            if (Website.TryCreate(url, out var site)) newSites.Add(site);
        return current.IsAtLeastAsStrict(newApps, newSites);
    }
}
