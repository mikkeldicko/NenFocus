using Microsoft.EntityFrameworkCore;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;
using static NenFocus.Infrastructure.Persistence.SlotQuery;

namespace NenFocus.Infrastructure.Persistence.Repositories;

public sealed class PlannedBlockRepository : IPlannedBlockRepository
{
    private readonly NenFocusDbContext _context;

    public PlannedBlockRepository(NenFocusDbContext context)
        => _context = context;

    // Every time filter here is one of TimeSlot.Filters applied to the block's Scheduled slot, so
    // the SQL is the Domain's own rule, never a copy (Gap 88). The sort key comes from there too.
    private static readonly System.Linq.Expressions.Expression<Func<PlannedBlock, Moment>> ByStart =
        OnSlot<PlannedBlock, Moment>(b => b.Scheduled, TimeSlot.Filters.ByStart);

    public async Task<IReadOnlyList<PlannedBlock>> GetForRangeAsync(
        TimeSlot range,
        CancellationToken ct = default)
    {
        // Excludes cancelled blocks — a
        // cancelled block (its owning contract was broken) has vacated its schedule
        // entirely and must never count as a live block to any caller. Without this,
        // StartSessionCommandHandler would refuse a spontaneous session because a
        // cancelled block "conflicts" with it, and ScheduledEnforcementService would
        // warn about a block that will never actually enforce.
        var blocks = await _context.PlannedBlocks
            .Where(OnSlot<PlannedBlock, bool>(b => b.Scheduled, TimeSlot.Filters.Overlaps(range)))
            .Where(b => b.CancelledAt == null)
            .OrderBy(ByStart)
            .ToListAsync(ct);

        return blocks.AsReadOnly();
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

    public Task<bool> HasFutureBlocksForPresetAsync(
        Guid presetId,
        Moment after,
        CancellationToken cancellationToken = default)
    {
        // Cancelled excluded: a broken contract's leftover block must not keep a preset
        // locked from editing/deletion just because it still has a future End date.
        return _context.PlannedBlocks
            .Where(Not(OnSlot<PlannedBlock, bool>(b => b.Scheduled, TimeSlot.Filters.HasEnded(after))))
            .AnyAsync(b => b.BlockPresetId == presetId && b.CancelledAt == null, cancellationToken);
    }

    public async Task<IReadOnlyList<FocusBlock>> GetUnresolvedFocusBlocksAsync(
        Moment asOf,
        CancellationToken cancellationToken = default)
    {
        // FocusBlock.IsUnresolved in SQL: the window-closed part is the Domain's own filter, so it
        // can't drift from the entity (Gap 88). Cancelled excluded: a block cancelled by breaking
        // its contract never ran its Ritual by design, not by oversight.
        return await _context.PlannedBlocks
            .Where(OnSlot<PlannedBlock, bool>(b => b.Scheduled, TimeSlot.Filters.HasEnded(asOf)))
            .OfType<FocusBlock>()
            .Where(b => b.RitualCompletedAt == null && b.CancelledAt == null)
            .OrderBy(OnSlot<FocusBlock, Moment>(b => b.Scheduled, TimeSlot.Filters.ByStart))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlannedBlock>> GetForContractAsync(
        Guid contractId,
        CancellationToken cancellationToken = default)
    {
        return await _context.PlannedBlocks
            .Where(b => b.Membership!.ContractId == contractId)
            .OrderBy(ByStart)
            .ToListAsync(cancellationToken);
    }
}
