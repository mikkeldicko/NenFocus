using Microsoft.EntityFrameworkCore;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Infrastructure.Persistence.Repositories;

public sealed class PlannedBlockRepository : IPlannedBlockRepository
{
    private readonly NenFocusDbContext _context;

    public PlannedBlockRepository(NenFocusDbContext context)
        => _context = context;

    public async Task<IReadOnlyList<PlannedBlock>> GetForDayAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        return await _context.PlannedBlocks
            .Where(b => b.IsPlanned && b.Start < dayEnd && b.End > dayStart)
            .OrderBy(b => b.Start)
            .ToListAsync(cancellationToken);
    }

    // Ensure the return type is Task<IReadOnlyList<PlannedBlock>>
    public async Task<IReadOnlyList<PlannedBlock>> GetForRangeAsync(
      DateTime from,
      DateTime to,
      CancellationToken ct = default)
    {
        // Removing the .Where(b => b.IsPlanned) check ensures the service sees ALL blocks
        var blocks = await _context.PlannedBlocks
            .Where(b => b.Start < to && b.End > from)
            .OrderBy(b => b.Start)
            .ToListAsync(ct);

        // Cast explicitly to the interface type
        return blocks.AsReadOnly();
    }
    public async Task<IReadOnlyList<PlannedBlock>> GetOverlappingAsync(
        DateTime start,
        DateTime end,
        BlockType? blockType = null,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.PlannedBlocks
            .Where(b => b.IsPlanned && b.Start < end && b.End > start);

        if (blockType.HasValue)
            query = query.Where(b => b.Type == blockType.Value);

        if (excludeId.HasValue)
            query = query.Where(b => b.Id != excludeId.Value);

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<PlannedBlock?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.PlannedBlocks
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    public async Task AddAsync(
        PlannedBlock block,
        CancellationToken cancellationToken = default)
    {
        await _context.PlannedBlocks.AddAsync(block, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        PlannedBlock block,
        CancellationToken cancellationToken = default)
    {
        _context.PlannedBlocks.Update(block);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var block = await _context.PlannedBlocks
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (block is null) return;

        _context.PlannedBlocks.Remove(block);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
