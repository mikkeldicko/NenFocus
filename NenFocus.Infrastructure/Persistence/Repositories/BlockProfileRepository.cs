using Microsoft.EntityFrameworkCore;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Infrastructure.Persistence.Repositories;

public sealed class BlockProfileRepository : IBlockProfileRepository
{
    private readonly NenFocusDbContext _context;

    public BlockProfileRepository(NenFocusDbContext context)
        => _context = context;

    public async Task<IReadOnlyList<BlockProfile>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.BlockProfiles
            .Include(p => p.BlockedApps)
            .Include(p => p.Websites)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<BlockProfile?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.BlockProfiles
            .Include(p => p.BlockedApps)
            .Include(p => p.Websites)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<BlockProfile>> GetByTagsAsync(
        IEnumerable<string> tags,
        CancellationToken cancellationToken = default)
    {
        var tagList = tags.ToList();

        // Load all profiles then filter by tags in memory
        // since tags are stored as a serialized string
        var all = await GetAllAsync(cancellationToken);

        return all
            .Where(p => p.Tags.Any(t => tagList.Contains(t,
                StringComparer.OrdinalIgnoreCase)))
            .ToList();
    }

    public async Task AddAsync(
        BlockProfile profile,
        CancellationToken cancellationToken = default)
    {
        await _context.BlockProfiles.AddAsync(profile, cancellationToken);
    }

    public async Task UpdateAsync(
        BlockProfile profile,
        CancellationToken cancellationToken = default)
    {
        _context.BlockProfiles.Update(profile);
    }

    public async Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var profile = await _context.BlockProfiles
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (profile is null) return;

        _context.BlockProfiles.Remove(profile);
    }

    public async Task<bool> ExistsWithNameAsync(
        string name,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.BlockProfiles
            .Where(p => p.Name.ToLower() == name.ToLower().Trim());

        if (excludeId.HasValue)
            query = query.Where(p => p.Id != excludeId.Value);

        return await query.AnyAsync(cancellationToken);
    }
}
