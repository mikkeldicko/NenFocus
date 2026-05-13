using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.Queries.GetPlannedBlocksForDay;

public sealed record GetPlannedBlocksForDayQuery
{
    public DateOnly Date { get; init; }
}

public sealed class GetPlannedBlocksForDayQueryHandler
    : IQueryHandler<GetPlannedBlocksForDayQuery, IReadOnlyList<PlannedBlockDto>>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockProfileRepository _profiles;

    public GetPlannedBlocksForDayQueryHandler(
        IPlannedBlockRepository blocks,
        IBlockProfileRepository profiles)
    {
        _blocks = blocks;
        _profiles = profiles;
    }

    public async Task<IReadOnlyList<PlannedBlockDto>> HandleAsync(
        GetPlannedBlocksForDayQuery query,
        CancellationToken cancellationToken = default)
    {
        var blocks = await _blocks.GetForDayAsync(query.Date, cancellationToken);

        // Load all profiles in one call to avoid N+1
        var allProfiles = await _profiles.GetAllAsync(cancellationToken);
        var profileMap = allProfiles.ToDictionary(p => p.Id);

        return blocks.Select(b =>
        {
            var profile = profileMap.TryGetValue(b.BlockProfileId, out var p) ? p : null;
            return new PlannedBlockDto
            {
                Id = b.Id,
                ProfileId = b.BlockProfileId,
                ProfileName = profile?.Name ?? "Unknown",
                // FIX: Use the Enum strings defined in your Domain.Enums.BlockType
                BlockType = b is RitualBlock
                    ? NenFocus.Domain.Enums.BlockType.Ritual
                    : NenFocus.Domain.Enums.BlockType.Background,
                Start = b.Start,
                End = b.End,
                IsAllowlistMode = profile?.WebsitePolicy == Domain.Enums.WebsitePolicy.Allowlist
            };
        }).ToList();
    }
}
