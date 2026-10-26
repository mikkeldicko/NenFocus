using FluentValidation;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckBlockPresetLocked;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.BlockPresets.UpdateBlockPreset;

public sealed class UpdateBlockPresetValidator : AbstractValidator<UpdateBlockPresetCommand>
{
    public UpdateBlockPresetValidator(
        IBlockPresetRepository presets,
        IQueryHandler<CheckBlockPresetLockedQuery, bool> locked)
    {
        // Both rules are the preset's: whether it's locked (IsLockedAt) and what may change while it is
        // (AllowsChangeWhileLocked) — Gap 89 Slice D.
        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (!await locked.HandleAsync(new CheckBlockPresetLockedQuery(command.PresetId), ct))
                    return true;

                var current = await presets.GetByIdAsync(command.PresetId, ct);
                return current is null || current.AllowsChangeWhileLocked(
                    command.Name, command.WebsiteFilterType, command.PresetType, AppsOf(command), SitesOf(command));
            })
            .WithMessage("This preset is in use — by the running session, a block still to come or a contract that still owes time of it — while locked, you may only add apps or websites. Removing entries, renaming, or changing its type or filter is not allowed until it's free.");

        // BlockedApp and Website say what can go on the list (Gap 112); worded in BlockListEntryRefusalDto.
        RuleForEach(x => x.BlockedApps)
            .Must(app => BlockedApp.Check(app) is null)
            .WithMessage((_, app) => BlockListEntryRefusalDto.ForApp(app, BlockedApp.Check(app)!.Value).Message);

        RuleForEach(x => x.Websites)
            .Must(site => Website.TryCreate(site, out _))
            .WithMessage((_, site) => BlockListEntryRefusalDto.ForWebsite(site).Message);
    }

    // The command's lists as values. Entries that aren't an app or a site are refused by the per-entry
    // rules anyway, so leaving them out here never decides the outcome.
    private static HashSet<BlockedApp> AppsOf(UpdateBlockPresetCommand command) =>
        command.BlockedApps.Where(a => BlockedApp.Check(a) is null).Select(a => new BlockedApp(a)).ToHashSet();

    private static HashSet<Website> SitesOf(UpdateBlockPresetCommand command)
    {
        var sites = new HashSet<Website>();
        foreach (var url in command.Websites)
            if (Website.TryCreate(url, out var site)) sites.Add(site);
        return sites;
    }
}
