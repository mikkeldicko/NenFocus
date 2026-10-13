using Microsoft.EntityFrameworkCore;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

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

    public async Task<bool> ExistsWithNameAsync(
        string name,
        BlockPresetType presetType,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.BlockPresets
            .Where(p => p.Name == name.Trim() && p.PresetType == presetType); // Name is NOCASE

        if (excludeId.HasValue)
            query = query.Where(p => p.Id != excludeId.Value);

        return await query.AnyAsync(cancellationToken);
    }

    public void Add(BlockPreset preset) => _context.BlockPresets.Add(preset);

    public void Remove(BlockPreset preset) => _context.BlockPresets.Remove(preset);
}
