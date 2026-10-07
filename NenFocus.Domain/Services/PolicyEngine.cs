using NenFocus.Domain.Constants;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

public sealed class PolicyEngine : IPolicyEngine
{
    public ActivePolicySlice ComputeSlice(
        Moment at,
        IEnumerable<BlockPreset> presets,
        Session? running,
        IEnumerable<PlannedBlock> blocks)
    {
        var allPresets = presets.ToList();
        // At most one session runs (Gap 92) — a list only so the merge below reads the same for blocks and sessions.
        var activeSessions = running is null ? new List<Session>() : [running];
        // IsActive(at) is the authoritative boundary check, independent of whatever window
        // the caller's own block query used to fetch candidates. ComputeActivePolicySliceQueryHandler's
        // range query deliberately over-fetches with a buffer around "now" (SQLite datetime
        // comparison slop) — PolicyEngine is what narrows that down precisely, so a block
        // whose window has already closed relative to `at` can never have its preset enforced,
        // even if the caller's looser query still returned it. A cancelled block is never active past
        // its cancel moment (PlannedBlock.EffectiveEnd), so no separate cancelled check here.
        var activeBlocks = blocks.Where(b => b.IsActive(at)).ToList();

        // `presets` is the same kind of over-fetched superset as `blocks` — the caller
        // resolves it from the coarse block/session query, not from activeBlocks/activeSessions
        // above. Narrowing it here (rather than trusting it as-is) mirrors the same
        // "coarse fetch, precise narrow" discipline the repositories
        // follow (MODULE_STANDARDS → narrow in SQL, decide in the entity). Without this, a FocusBlock whose session ended
        // early correctly drops out of activeBlocks but its preset's blocked sites/apps kept
        // being reapplied every heartbeat tick anyway, for as long as `at` was still inside
        // the block's *original* Start/End window.
        // A session whose break lifts blocking right now (Session.LiftsBlockingAt — the
        // session already knows whether its contract keeps blocking through breaks, Gap 64)
        // has its own preset contribution excluded, together with its originating block's.
        // An unrelated, merely co-occurring Background Block is never affected (it isn't
        // sourced from either exclusion set below).
        var pausedBlockIds = new HashSet<Guid>();
        var pausedSessionIds = new HashSet<Guid>();

        foreach (var session in activeSessions.Where(s => s.LiftsBlockingAt(at)))
        {
            pausedSessionIds.Add(session.Id);
            if (session.OriginatingBlockId is { } originatingBlockId)
                pausedBlockIds.Add(originatingBlockId);
        }

        var activePresetIds = activeBlocks
            .Where(b => !pausedBlockIds.Contains(b.Id))
            .Select(b => b.BlockPresetId)
            .Concat(activeSessions
                .Where(s => !pausedSessionIds.Contains(s.Id))
                .Select(s => s.BlockPresetId))
            .ToHashSet();
        var activePresets = allPresets.Where(p => activePresetIds.Contains(p.Id)).ToList();

        if (activePresets.Count == 0 && activeSessions.Count == 0 && activeBlocks.Count == 0)
            return new ActivePolicySlice { SliceStart = at, SliceEnd = at + TimeSpan.FromMinutes(1) };

        var tempApps = activeSessions
            .Where(s => !pausedSessionIds.Contains(s.Id))
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
            .Where(s => !pausedSessionIds.Contains(s.Id))
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

        return new ActivePolicySlice
        {
            ActiveSessionId = activeSessions.FirstOrDefault()?.Id,
            SliceStart = at,
            SliceEnd = activeBlocks.Any() ? activeBlocks.Max(b => b.Scheduled.End) : at + TimeSpan.FromMinutes(1),
            WebsiteFilterTypeMode = effectivePolicy,
            AllowedSites = allowedSites,
            BlockedSites = blockedSites,
            BlockedApps = blockedApps
        };
    }
}
