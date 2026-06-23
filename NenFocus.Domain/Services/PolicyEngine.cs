using NenFocus.Domain.Constants;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

public sealed class PolicyEngine : IPolicyEngine
{
    public ActivePolicySlice ComputeSlice(
        DateTime at,
        IEnumerable<BlockProfile> profiles,
        IEnumerable<Session> sessions,
        IEnumerable<PlannedBlock> blocks)
    {
        var activeProfiles = profiles.ToList();
        var activeSessions = sessions.ToList();
        var activeBlocks = blocks.ToList();

        if (activeProfiles.Count == 0 && activeSessions.Count == 0)
            return new ActivePolicySlice { SliceStart = at, SliceEnd = at.AddMinutes(1) };

        var blockedApps = activeProfiles
            .SelectMany(p => p.BlockedApps.Select(a => ProcessUtils.NormalizeProcessName(a.ProcessName)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var session in activeSessions)
            foreach (var app in session.TemporaryBlockedApps)
                blockedApps.Add(ProcessUtils.NormalizeProcessName(app));

        var effectivePolicy = activeProfiles.Any(p => p.WebsitePolicy == WebsitePolicy.Allowlist)
            ? WebsitePolicy.Allowlist
            : WebsitePolicy.Blocklist;

        HashSet<string> allowedSites = new();
        HashSet<string> blockedSites = new();

        if (effectivePolicy == WebsitePolicy.Allowlist)
        {
            var allowlistProfiles = activeProfiles.Where(p => p.WebsitePolicy == WebsitePolicy.Allowlist).ToList();

            var intersectList = allowlistProfiles[0].Websites.Select(s => s.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in allowlistProfiles.Skip(1))
                intersectList.IntersectWith(profile.Websites.Select(s => s.Url));

            var unionOfBlocklists = activeProfiles
                .Where(p => p.WebsitePolicy == WebsitePolicy.Blocklist)
                .SelectMany(p => p.Websites.Select(s => s.Url))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            intersectList.ExceptWith(unionOfBlocklists);
            allowedSites = intersectList;
            blockedSites = unionOfBlocklists;
        }
        else
        {
            blockedSites = activeProfiles.SelectMany(p => p.Websites.Select(s => s.Url)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var session in activeSessions)
            foreach (var site in session.TemporaryBlockedSites)
                blockedSites.Add(site);

        var activePlannedBlock = activeBlocks.OrderByDescending(b => b is FocusBlock).FirstOrDefault();
        BlockType? activeBlockType = activePlannedBlock switch
        {
            FocusBlock => BlockType.Focus,
            BackgroundBlock => BlockType.Background,
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
            WebsitePolicyMode = effectivePolicy,
            AllowedSites = allowedSites,
            BlockedSites = blockedSites,
            BlockedApps = blockedApps,
            ActiveBlockType = activeBlockType,
            FocusSessionCompleted = focusSessionCompleted
        };
    }
}
