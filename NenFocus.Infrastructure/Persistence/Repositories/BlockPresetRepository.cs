using Microsoft.EntityFrameworkCore;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Infrastructure.Persistence.Repositories;

public sealed class BlockPresetRepository : IBlockPresetRepository
{
    private readonly NenFocusDbContext _context;

    public BlockPresetRepository(NenFocusDbContext context)
        => _context = context;

    public async Task<IReadOnlyList<BlockPreset>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.BlockPresets
            .Include(p => p.BlockedApps)
            .Include(p => p.Websites)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<BlockPreset?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.BlockPresets
            .Include(p => p.BlockedApps)
            .Include(p => p.Websites)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<BlockPreset>> GetByTagsAsync(
        IEnumerable<string> tags,
        CancellationToken cancellationToken = default)
    {
        var tagList = tags.ToList();

        // Load all presets then filter by tags in memory
        // since tags are stored as a serialized string
        var all = await GetAllAsync(cancellationToken);

        return all
            .Where(p => p.Tags.Any(t => tagList.Contains(t,
                StringComparer.OrdinalIgnoreCase)))
            .ToList();
    }

    public async Task AddAsync(
        BlockPreset preset,
        CancellationToken cancellationToken = default)
    {
        await _context.BlockPresets.AddAsync(preset, cancellationToken);
    }

    public async Task UpdateAsync(
        BlockPreset preset,
        CancellationToken cancellationToken = default)
    {
        // Entity was loaded by this same context instance, so the change tracker
        // already has a snapshot. EF Core detects scalar and OwnsMany changes
        // automatically — calling Update() here would force all owned entities to
        // Modified state, turning inserts for new BlockedApps into silent no-ops.
    }

    public async Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var preset = await _context.BlockPresets
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (preset is null) return;

        _context.BlockPresets.Remove(preset);
    }

    public async Task<bool> ExistsWithNameAsync(
        string name,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.BlockPresets
            .Where(p => p.Name.ToLower() == name.ToLower().Trim());

        if (excludeId.HasValue)
            query = query.Where(p => p.Id != excludeId.Value);

        return await query.AnyAsync(cancellationToken);
    }
}
