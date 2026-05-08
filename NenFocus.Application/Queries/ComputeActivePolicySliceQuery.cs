using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Aggregates;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using System.Linq;

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
        // 1. Get active planned blocks
        var activeBlocks = (await _blocks.GetForRangeAsync(
          query.At.AddSeconds(-1),
          query.At.AddSeconds(1),
          cancellationToken)).ToList();

        // 2. Get active impromptu sessions
        var activeSessions = await _sessions.GetActiveSessionsAsync(cancellationToken);

        // If nothing is happening, return empty
        if (activeBlocks.Count == 0 && activeSessions.Count == 0)
            return new PolicySliceDto();

        // 3. Gather all Profile IDs from BOTH sources
        var profileIdsFromBlocks = activeBlocks.Select(b => b.BlockProfileId);
        var profileIdsFromSessions = activeSessions.Select(s => s.BlockProfileId);

        var allProfileIds = profileIdsFromBlocks
            .Concat(profileIdsFromSessions)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList(); // Materialize to list to avoid Concat errors

        // 4. Load the profiles (using Entities namespace)
        var profiles = new Dictionary<Guid, BlockProfile>();
        foreach (var id in allProfileIds)
        {
            var profile = await _profiles.GetByIdAsync(id, cancellationToken);
            if (profile is BlockProfile bp) // Type check/cast to be safe
                profiles[id] = bp;
        }

        var activeProfiles = profiles.Values.ToList();

        // 5. Apps: union across all active profiles
        var blockedApps = activeProfiles
            .SelectMany(p => p.BlockedApps.Select(a => a.ProcessName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 6. Websites: check if any allowlist is active
        var allowlistProfiles = activeProfiles
            .Where(p => p.WebsitePolicy == WebsitePolicy.Allowlist)
            .ToList();

        bool isAllowlistMode = allowlistProfiles.Count > 0;
        HashSet<string> allowedSites = [];
        HashSet<string> blockedSites = [];

        if (isAllowlistMode)
        {
            var allAllowlists = allowlistProfiles
                .Select(p => p.Websites.Select(s => s.Url).ToHashSet(StringComparer.OrdinalIgnoreCase))
                .ToList();

            allowedSites = allAllowlists.First();
            foreach (var list in allAllowlists.Skip(1))
                allowedSites.IntersectWith(list);

            blockedSites = activeProfiles
                .Where(p => p.WebsitePolicy == WebsitePolicy.Blocklist)
                .SelectMany(p => p.Websites.Select(s => s.Url))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            blockedSites = activeProfiles
                .SelectMany(p => p.Websites.Select(s => s.Url))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return new PolicySliceDto
        {
            ActiveSessionId = activeSessions.FirstOrDefault()?.Id, // Add this line
            SliceStart = query.At,
            SliceEnd = activeBlocks.Any() ? activeBlocks.Max(b => b.End) : query.At.AddMinutes(1),
            WebsiteAllowlistMode = isAllowlistMode,
            AllowedSites = allowedSites,
            BlockedSites = blockedSites,
            BlockedApps = blockedApps
        };
    }
}