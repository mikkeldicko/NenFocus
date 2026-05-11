using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Aggregates;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Queries.ComputeActivePolicySlice;

public sealed record ComputeActivePolicySliceQuery
{
    public DateTime At { get; init; }
}

public sealed class ComputeActivePolicySliceQueryHandler
    : IQueryHandler<ComputeActivePolicySliceQuery, PolicySliceDto>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockProfileRepository _profiles;
    private readonly ISessionRepository _sessions;

    public ComputeActivePolicySliceQueryHandler(
        IPlannedBlockRepository blocks,
        IBlockProfileRepository profiles,
        ISessionRepository sessions)
    {
        _blocks = blocks;
        _profiles = profiles;
        _sessions = sessions;
    }

    public async Task<PolicySliceDto> HandleAsync(
        ComputeActivePolicySliceQuery query,
        CancellationToken cancellationToken = default)
    {
        // 1. Get active planned blocks and impromptu sessions
        var activeBlocks = (await _blocks.GetForRangeAsync(
            query.At.AddSeconds(-1),
            query.At.AddSeconds(1),
            cancellationToken)).ToList();

        var activeSessions = await _sessions.GetActiveSessionsAsync(cancellationToken);

        if (activeBlocks.Count == 0 && activeSessions.Count == 0)
            return new PolicySliceDto();

        // 2. Load the actual Profile entities
        var allProfileIds = activeBlocks.Select(b => b.BlockProfileId)
            .Concat(activeSessions.Select(s => s.BlockProfileId))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var activeProfiles = new List<BlockProfile>();
        foreach (var id in allProfileIds)
        {
            if (await _profiles.GetByIdAsync(id, cancellationToken) is BlockProfile bp)
                activeProfiles.Add(bp);
        }

        // 3. Apps: Always a Union
        var blockedApps = activeProfiles
            .SelectMany(p => p.BlockedApps.Select(a => a.ProcessName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 4. Decide the Effective Website Policy
        var effectivePolicy = activeProfiles.Any(p => p.WebsitePolicy == WebsitePolicy.Allowlist)
            ? WebsitePolicy.Allowlist
            : WebsitePolicy.Blocklist;

        HashSet<string> allowedSites = [];
        HashSet<string> blockedSites = [];

        // 5. Use the Enum to decide how to build the lists
        switch (effectivePolicy)
        {
            case WebsitePolicy.Allowlist:
                var allowlistProfiles = activeProfiles.Where(p => p.WebsitePolicy == WebsitePolicy.Allowlist).ToList();

                // Intersection of all allowlists
                var firstList = allowlistProfiles[0].Websites.Select(s => s.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var profile in allowlistProfiles.Skip(1))
                {
                    firstList.IntersectWith(profile.Websites.Select(s => s.Url));
                }

                // Get Union of all blocklists
                var unionOfBlocklists = activeProfiles
                    .Where(p => p.WebsitePolicy == WebsitePolicy.Blocklist)
                    .SelectMany(p => p.Websites.Select(s => s.Url))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // CRITICAL: Remove any blocked sites from the allowed list
                firstList.ExceptWith(unionOfBlocklists);

                allowedSites = firstList;
                blockedSites = unionOfBlocklists; // Still track these for reporting
                break;
        }

        return new PolicySliceDto
        {
            ActiveSessionId = activeSessions.FirstOrDefault()?.Id,
            SliceStart = query.At,
            SliceEnd = activeBlocks.Any() ? activeBlocks.Max(b => b.End) : query.At.AddMinutes(1),
            WebsitePolicyMode = effectivePolicy,
            AllowedSites = allowedSites,
            BlockedSites = blockedSites,
            BlockedApps = blockedApps
        };
    }
}