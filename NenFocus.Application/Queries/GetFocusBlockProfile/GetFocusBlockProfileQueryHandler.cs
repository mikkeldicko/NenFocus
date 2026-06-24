using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.Queries.GetFocusBlockProfile;

public sealed class GetFocusBlockProfileQueryHandler
    : IQueryHandler<GetFocusBlockProfileQuery, BlockProfileDto?>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockProfileRepository _profiles;

    public GetFocusBlockProfileQueryHandler(
        IPlannedBlockRepository blocks,
        IBlockProfileRepository profiles)
    {
        _blocks = blocks;
        _profiles = profiles;
    }

    public async Task<BlockProfileDto?> HandleAsync(GetFocusBlockProfileQuery query, CancellationToken ct)
    {
        var block = await _blocks.GetByIdAsync(query.FocusBlockId, ct) as FocusBlock;
        if (block is null) return null;

        var profile = await _profiles.GetByIdAsync(block.BlockProfileId, ct);
        if (profile is null) return null;

        return new BlockProfileDto
        {
            Id = profile.Id,
            Name = profile.Name,
            Tags = profile.Tags.ToList(),
            ProfileType = profile.ProfileType,
            WebsitePolicy = profile.WebsitePolicy,
            BlockedApps = profile.BlockedApps.Select(a => a.ProcessName).ToList(),
            Websites = profile.Websites.Select(s => s.Url).ToList()
        };
    }
}
