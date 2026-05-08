using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetAllBlockProfiles;

public sealed record GetAllBlockProfilesQuery
{
    /// <summary>Optional tag filter. Returns all profiles if null.</summary>
    public string? Tag { get; init; }
}

public sealed class GetAllBlockProfilesQueryHandler
    : IQueryHandler<GetAllBlockProfilesQuery, IReadOnlyList<BlockProfileDto>>
{
    private readonly IBlockProfileRepository _profiles;

    public GetAllBlockProfilesQueryHandler(IBlockProfileRepository profiles)
        => _profiles = profiles;

    public async Task<IReadOnlyList<BlockProfileDto>> HandleAsync(
        GetAllBlockProfilesQuery query,
        CancellationToken cancellationToken = default)
    {
        var profiles = string.IsNullOrWhiteSpace(query.Tag)
            ? await _profiles.GetAllAsync(cancellationToken)
            : await _profiles.GetByTagsAsync([query.Tag], cancellationToken);

        return profiles.Select(p => new BlockProfileDto
        {
            Id = p.Id,
            Name = p.Name,
            Tags = p.Tags,
            WebsitePolicy = p.WebsitePolicy.ToString(),
            BlockedApps = p.BlockedApps.Select(a => a.ProcessName).ToList(),
            Websites = p.Websites.Select(s => s.Url).ToList()
        }).ToList();
    }
}
