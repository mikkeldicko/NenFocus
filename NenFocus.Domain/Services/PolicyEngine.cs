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
        IEnumerable<PlannedBlock> blocks,
        IEnumerable<NenContract>? contracts = null)
    {
        var allPresets = presets.ToList();
        var activeSessions = sessions.ToList();
        var allContracts = contracts?.ToList() ?? [];
        // IsActive(at) is the authoritative boundary check, independent of whatever window
        // the caller's own block query used to fetch candidates. ComputeActivePolicySliceQueryHandler's
        // range query deliberately over-fetches with a buffer around "now" (SQLite datetime
        // comparison slop) — PolicyEngine is what narrows that down precisely, so a block
        // whose window has already closed relative to `at` can never become ActiveFocusBlockId,
        // even if the caller's looser query still returned it.
        var activeBlocks = blocks.Where(b => !b.CancelledAt.HasValue && b.IsActive(at)).ToList();

        // `presets` is the same kind of over-fetched superset as `blocks` — the caller
        // resolves it from the coarse block/session query, not from activeBlocks/activeSessions
        // above. Narrowing it here (rather than trusting it as-is) mirrors the same
        // "coarse fetch, precise narrow" discipline PlannedBlockRepository.GetOverlappingAsync
        // already applies via EffectiveEnd. Without this, a FocusBlock whose session ended
        // early correctly drops out of activeBlocks but its preset's blocked sites/apps kept
        // being reapplied every heartbeat tick anyway, for as long as `at` was still inside
        // the block's *original* Start/End window.
        // Resolve pause-during-break for each OnBreak session before folding presets
        // together — a session/block whose break is currently pausing enforcement has
        // its own preset contribution excluded entirely, while an unrelated, merely
        // co-occurring Background Block is never affected (it isn't sourced from
        // either exclusion set below). The global Settings default doesn't exist yet
        // and is hardcoded permissive (true); a sealed contract can only ever narrow
        // that toward stricter (keep blocking), never loosen it further. A Bonus block
        // (FocusBlock.ContractId == null) always falls back to the hardcoded default,
        // never a contract's setting — it was never actually committed to one.
        var pausedBlockIds = new HashSet<Guid>();
        var pausedSessionIds = new HashSet<Guid>();

        foreach (var session in activeSessions.Where(s => s.Status == SessionStatus.OnBreak))
        {
            var originatingBlock = session.OriginatingBlockId.HasValue
                ? activeBlocks.OfType<FocusBlock>().FirstOrDefault(b => b.Id == session.OriginatingBlockId.Value)
                : null;

            var pauseAllowed = originatingBlock?.ContractId is { } contractId
                ? allContracts.FirstOrDefault(c => c.Id == contractId)?.PauseEnforcementDuringBreak ?? true
                : true;

            if (!pauseAllowed) continue;

            pausedSessionIds.Add(session.Id);
            if (originatingBlock is not null)
                pausedBlockIds.Add(originatingBlock.Id);
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
            return new ActivePolicySlice { SliceStart = at, SliceEnd = at.AddMinutes(1) };

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
