using NenFocus.Domain.Constants;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

public sealed class PolicyEngine : IPolicyEngine
{
    public ActivePolicySlice ComputeSlice(
        DateTime at,
        IEnumerable<BlockPreset> presets,
        IEnumerable<Session> sessions,
        IEnumerable<PlannedBlock> blocks)
    {
        var activePresets = presets.ToList();
        var activeSessions = sessions.ToList();
        var activeBlocks = blocks.Where(b => !b.CancelledAt.HasValue).ToList();

        if (activePresets.Count == 0 && activeSessions.Count == 0 && activeBlocks.Count == 0)
            return new ActivePolicySlice { SliceStart = at, SliceEnd = at.AddMinutes(1) };

        var tempApps = activeSessions
            .SelectMany(s => s.TemporaryBlockedApps.Select(ProcessUtils.NormalizeProcessName));

        var blockedApps = activePresets
            .SelectMany(p => p.BlockedApps.Select(a => ProcessUtils.NormalizeProcessName(a.ProcessName)))
            .Concat(tempApps)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var effectivePolicy = activePresets.Any(p => p.WebsiteFilterType == WebsiteFilterType.Allowlist)
            ? WebsiteFilterType.Allowlist
            : WebsiteFilterType.Blocklist;

        HashSet<string> allowedSites = new();
        HashSet<string> blockedSites = new();

        var tempSites = activeSessions
            .SelectMany(s => s.TemporaryBlockedSites)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (effectivePolicy == WebsiteFilterType.Allowlist)
        {
            var allowlistPresets = activePresets.Where(p => p.WebsiteFilterType == WebsiteFilterType.Allowlist).ToList();

            var intersectList = allowlistPresets[0].Websites.Select(s => s.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var preset in allowlistPresets.Skip(1))
                intersectList.IntersectWith(preset.Websites.Select(s => s.Url));

            var unionOfBlocklists = activePresets
                .Where(p => p.WebsiteFilterType == WebsiteFilterType.Blocklist)
                .SelectMany(p => p.Websites.Select(s => s.Url))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Temporary sites are always blocked — remove them from the allowed set.
            intersectList.ExceptWith(unionOfBlocklists);
            intersectList.ExceptWith(tempSites);
            allowedSites = intersectList;
            blockedSites = unionOfBlocklists;
            blockedSites.UnionWith(tempSites);
        }
        else
        {
            blockedSites = activePresets.SelectMany(p => p.Websites.Select(s => s.Url)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            blockedSites.UnionWith(tempSites);
        }

        var activePlannedBlock = activeBlocks.OrderByDescending(b => b is FocusBlock).FirstOrDefault();
        BlockPresetType? activeBlockPresetType = activePlannedBlock switch
        {
            FocusBlock => BlockPresetType.Focus,
            BackgroundBlock => BlockPresetType.Background,
            _ => null
        };

        // A Focus Block whose Ritual was completed but whose session has since ended —
        // the scheduler must not re-apply enforcement or re-trigger the Ritual.
        var focusBlock = activeBlocks.OfType<FocusBlock>().FirstOrDefault();
        bool focusSessionCompleted = focusBlock?.RitualCompletedAt != null && !activeSessions.Any();

        return new ActivePolicySlice
        {
            ActiveSessionId = activeSessions.FirstOrDefault()?.Id,
            SliceStart = at,
            SliceEnd = activeBlocks.Any() ? activeBlocks.Max(b => b.End) : at.AddMinutes(1),
            WebsiteFilterTypeMode = effectivePolicy,
            AllowedSites = allowedSites,
            BlockedSites = blockedSites,
            BlockedApps = blockedApps,
            ActiveBlockPresetType = activeBlockPresetType,
            ActiveFocusBlockId = focusBlock?.Id,
            FocusSessionCompleted = focusSessionCompleted
        };
    }
}
