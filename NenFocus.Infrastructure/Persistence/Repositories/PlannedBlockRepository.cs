using Microsoft.EntityFrameworkCore;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

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
            .Where(b => b.Start < dayEnd && b.End > dayStart)
            .OrderBy(b => b.Start)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlannedBlock>> GetForRangeAsync(
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        var blocks = await _context.PlannedBlocks
            .Where(b => b.Start < to && b.End > from)
            .OrderBy(b => b.Start)
            .ToListAsync(ct);

        return blocks.AsReadOnly();
    }

    public async Task<IReadOnlyList<PlannedBlock>> GetOverlappingAsync(
        DateTime start,
        DateTime end,
        bool ritualOnly = false,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.PlannedBlocks
            .Where(b => b.Start < end && b.End > start);

        if (excludeId.HasValue)
            query = query.Where(b => b.Id != excludeId.Value);

        var blocks = await query.ToListAsync(cancellationToken);

        if (ritualOnly)
            return blocks.OfType<FocusBlock>().ToList<PlannedBlock>();

        return blocks;
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
    }

    public Task UpdateAsync(
        PlannedBlock block,
        CancellationToken cancellationToken = default)
    {
        _context.PlannedBlocks.Update(block);
        return Task.CompletedTask;
    }

    public async Task SaveAsync(
        PlannedBlock block,
        CancellationToken cancellationToken = default)
    {
        var exists = await _context.PlannedBlocks
            .AnyAsync(b => b.Id == block.Id, cancellationToken);

        if (!exists)
            await _context.PlannedBlocks.AddAsync(block, cancellationToken);
        else
            _context.PlannedBlocks.Update(block);
    }

    public async Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var block = await _context.PlannedBlocks
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (block is null) return;

        _context.PlannedBlocks.Remove(block);
    }

    public Task<bool> HasFutureBlocksForProfileAsync(
        Guid profileId,
        DateTime after,
        CancellationToken cancellationToken = default)
    {
        return _context.PlannedBlocks
            .AnyAsync(b => b.BlockProfileId == profileId && b.End > after, cancellationToken);
    }

    public Task<bool> HasActiveFocusBlockAsync(
        DateTime at,
        CancellationToken cancellationToken = default)
    {
        return _context.PlannedBlocks
            .OfType<FocusBlock>()
            .AnyAsync(b => b.Start <= at && b.End > at, cancellationToken);
    }

    public async Task<IReadOnlyList<FocusBlock>> GetUnresolvedFocusBlocksAsync(
        DateTime asOf,
        CancellationToken cancellationToken = default)
    {
        return await _context.PlannedBlocks
            .OfType<FocusBlock>()
            .Where(b => b.End < asOf && b.RitualCompletedAt == null)
            .OrderBy(b => b.Start)
            .ToListAsync(cancellationToken);
    }
}
