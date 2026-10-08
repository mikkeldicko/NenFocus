using Microsoft.EntityFrameworkCore;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Infrastructure.Persistence.Repositories;

public sealed class BreakTimerPresetRepository : IBreakTimerPresetRepository
{
    private readonly NenFocusDbContext _context;

    public BreakTimerPresetRepository(NenFocusDbContext context)
        => _context = context;

    public async Task<IReadOnlyList<BreakTimerPreset>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.BreakTimerPresets
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<BreakTimerPreset?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.BreakTimerPresets
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task AddAsync(
        BreakTimerPreset preset,
        CancellationToken cancellationToken = default)
    {
        await _context.BreakTimerPresets.AddAsync(preset, cancellationToken);
    }

    public Task UpdateAsync(
        BreakTimerPreset preset,
        CancellationToken cancellationToken = default)
    {
        // Entity was loaded by this same context instance; EF Core's change tracker
        // already has a snapshot and detects scalar/owned-type changes automatically.
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var preset = await _context.BreakTimerPresets
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (preset is null) return;

        _context.BreakTimerPresets.Remove(preset);
    }

    public async Task<bool> ExistsWithNameAsync(
        string name,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.BreakTimerPresets
            .Where(p => p.Name == name.Trim()); // Name is NOCASE

        if (excludeId.HasValue)
            query = query.Where(p => p.Id != excludeId.Value);

        return await query.AnyAsync(cancellationToken);
    }
}
