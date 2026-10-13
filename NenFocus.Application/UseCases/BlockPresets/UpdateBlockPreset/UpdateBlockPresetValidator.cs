using FluentValidation;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Constants;
using NenFocus.Domain.Entities;

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

        RuleForEach(x => x.BlockedApps)
            .Must(app => !SystemSafetyConstants.UnkillableProcesses.Contains(ProcessUtils.NormalizeProcessName(app)))
            .WithMessage((cmd, app) => $"'{app}' is a critical system process and cannot be blocked.");
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

        var newApps = command.BlockedApps.Select(a => a.Trim()).ToHashSet();
        var newSites = command.Websites.Select(u => u.Trim().ToLowerInvariant()).ToHashSet();
        return current.IsAtLeastAsStrict(newApps, newSites);
    }
}
