using NenFocus.Domain.Aggregates;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Utils;
using NenFocus.Domain.ValueObjects; // Ensure this is correct

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

        // 1. App Blocking Logic
        var blockedApps = activeProfiles
            .SelectMany(p => p.BlockedApps.Select(a => ProcessUtils.NormalizeProcessName(a.ProcessName)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var session in activeSessions)
            foreach (var app in session.TemporaryBlockedApps)
                blockedApps.Add(ProcessUtils.NormalizeProcessName(app));

        // 2. Website Policy Precedence
        var effectivePolicy = activeProfiles.Any(p => p.WebsitePolicy == WebsitePolicy.Allowlist)
            ? WebsitePolicy.Allowlist
            : WebsitePolicy.Blocklist;

        HashSet<string> allowedSites = new();
        HashSet<string> blockedSites = new();

        if (effectivePolicy == WebsitePolicy.Allowlist)
        {
            var allowlistProfiles = activeProfiles.Where(p => p.WebsitePolicy == WebsitePolicy.Allowlist).ToList();
            if (allowlistProfiles.Any())
            {
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
        }
        else
        {
            blockedSites = activeProfiles.SelectMany(p => p.Websites.Select(s => s.Url)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var session in activeSessions)
            foreach (var site in session.TemporaryBlockedSites)
                blockedSites.Add(site);

        // 3. Block Type Priority
        var activePlannedBlock = activeBlocks.OrderByDescending(b => b is RitualBlock).FirstOrDefault();
        BlockType? activeBlockType = activePlannedBlock switch
        {
            RitualBlock => BlockType.Ritual,
            BackgroundBlock => BlockType.Background,
            _ => null
        };

        return new ActivePolicySlice
        {
            ActiveSessionId = activeSessions.FirstOrDefault()?.Id,
            SliceStart = at,
            SliceEnd = activeBlocks.Any() ? activeBlocks.Max(b => b.End) : at.AddMinutes(1),
            WebsitePolicyMode = effectivePolicy,
            AllowedSites = allowedSites,
            BlockedSites = blockedSites,
            BlockedApps = blockedApps,
            ActiveBlockType = activeBlockType
        };
    }
}