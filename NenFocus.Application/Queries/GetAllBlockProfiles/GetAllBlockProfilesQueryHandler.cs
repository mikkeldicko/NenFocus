using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetAllBlockProfiles;

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

        IEnumerable<Domain.Entities.BlockProfile> filtered = profiles;
        if (query.ProfileType.HasValue)
            filtered = profiles.Where(p => p.ProfileType == query.ProfileType.Value);

        return filtered.Select(BlockProfileDto.FromEntity).ToList();
    }
}
