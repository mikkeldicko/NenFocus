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
        // date is the user's LOCAL calendar day (every caller derives it from a local
        // DateTime — e.g. PlannerPageViewModel.LoadWeekAsync's WeekStart.AddDays(i)) — but
        // b.Start/b.End are stored UTC. Anchoring the boundary with DateTimeKind.Utc
        // directly on that local date (the previous bug here) silently mis-windowed every
        // query for any non-UTC user: a cross-midnight block's tail could fall on the
        // wrong side of the query's UTC-anchored boundary and never surface on the day it
        // actually occupies. Exclusive upper bound (next local midnight) rather than
        // TimeOnly.MaxValue, matching the [start, end) semantics used everywhere else in
        // this domain — also sidesteps TimeOnly's own inability to represent "24:00".
        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var dayEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();

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
        // Excludes cancelled blocks for the same reason GetOverlappingAsync does — a
        // cancelled block (its owning contract was broken) has vacated its schedule
        // entirely and must never count as a live block to any caller. Without this,
        // StartSessionCommandHandler would refuse a spontaneous session because a
        // cancelled block "conflicts" with it, and ScheduledEnforcementService would
        // warn about a block that will never actually enforce.
        var blocks = await _context.PlannedBlocks
            .Where(b => b.Start < to && b.End > from && b.CancelledAt == null)
            .OrderBy(b => b.Start)
            .ToListAsync(ct);

        return blocks.AsReadOnly();
    }

    public async Task<IReadOnlyList<PlannedBlock>> GetOverlappingAsync(
        DateTime start,
        DateTime end,
        DateTime asOf,
        bool ritualOnly = false,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        // Filters on raw Start/End first — a safe superset, since EffectiveEnd(asOf) is
        // never later than End — then refines in-memory against EffectiveEnd, which needs
        // the materialized FocusBlock/BackgroundBlock type to evaluate. A cancelled block
        // (its owning contract was broken) has vacated its schedule entirely — same
        // "stops enforcing, stops appearing" treatment CancelledAt already gets in
        // ComputeActivePolicySliceQueryHandler and GetPlannedBlocksForDayQueryHandler —
        // so it must never count as a scheduling collision for new/moved blocks either.
        var query = _context.PlannedBlocks
            .Where(b => b.Start < end && b.End > start && b.CancelledAt == null);

        if (excludeId.HasValue)
            query = query.Where(b => b.Id != excludeId.Value);

        var blocks = await query.ToListAsync(cancellationToken);

        var stillReserved = blocks.Where(b => b.EffectiveEnd(asOf) > start).ToList();

        if (ritualOnly)
            return stillReserved.OfType<FocusBlock>().ToList<PlannedBlock>();

        return stillReserved;
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
        DateTime after,
        CancellationToken cancellationToken = default)
    {
        // Cancelled excluded: a broken contract's leftover block must not keep a preset
        // locked from editing/deletion just because it still has a future End date.
        return _context.PlannedBlocks
            .AnyAsync(b => b.BlockPresetId == presetId && b.End > after && b.CancelledAt == null, cancellationToken);
    }

    public async Task<IReadOnlyList<FocusBlock>> GetUnresolvedFocusBlocksAsync(
        DateTime asOf,
        CancellationToken cancellationToken = default)
    {
        // SQL mirror of FocusBlock.IsUnresolved (the Domain rule), kept for query speed —
        // PlannedBlockRepositoryTests checks the two agree. Cancelled excluded: a block
        // cancelled by breaking its contract never ran its Ritual by design, not by oversight.
        return await _context.PlannedBlocks
            .OfType<FocusBlock>()
            .Where(b => b.End < asOf && b.RitualCompletedAt == null && b.CancelledAt == null)
            .OrderBy(b => b.Start)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlannedBlock>> GetForContractAsync(
        Guid contractId,
        CancellationToken cancellationToken = default)
    {
        return await _context.PlannedBlocks
            .Where(b => b.ContractId == contractId)
            .OrderBy(b => b.Start)
            .ToListAsync(cancellationToken);
    }
}
