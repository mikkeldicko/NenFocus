using Microsoft.EntityFrameworkCore;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Infrastructure.Persistence.Repositories;

public sealed class NenContractRepository : INenContractRepository
{
    private readonly NenFocusDbContext _context;

    public NenContractRepository(NenFocusDbContext context)
        => _context = context;

    public async Task<NenContract?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.NenContracts
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task AddAsync(NenContract contract, CancellationToken cancellationToken = default)
    {
        await _context.NenContracts.AddAsync(contract, cancellationToken);
    }

    public Task UpdateAsync(NenContract contract, CancellationToken cancellationToken = default)
    {
        // Entity was loaded by this same context instance; EF Core's change tracker
        // already has a snapshot and detects scalar/converted-property changes automatically.
        return Task.CompletedTask;
    }

    public async Task SaveAsync(NenContract contract, CancellationToken cancellationToken = default)
    {
        // Mirrors PlannedBlockRepository.SaveAsync's exact shape — a DB existence check,
        // not an Entry().State check, so a detached entity whose Id already exists in the
        // store is correctly routed to Update rather than a doomed Add. Safe to call
        // Update() unconditionally here even when already tracked: unlike BlockPreset,
        // NenContract has no OwnsMany child collections, so there's no risk of forcing
        // owned entities into Modified state and turning inserts into no-ops.
        var exists = await _context.NenContracts
            .AnyAsync(c => c.Id == contract.Id, cancellationToken);

        if (!exists)
            await _context.NenContracts.AddAsync(contract, cancellationToken);
        else
            _context.NenContracts.Update(contract);
    }

    public async Task<IReadOnlyList<NenContract>> GetDueForActivationAsync(
        DateTime asOf,
        CancellationToken cancellationToken = default)
    {
        // Mirrors NenContract.IsDueForActivation(DateOnly) exactly — the canonical
        // definition lives on the entity; this query must not reinvent it.
        var asOfDate = DateOnly.FromDateTime(asOf);
        return await _context.NenContracts
            .Where(c => c.Status == NenContractStatus.Scheduled && asOfDate >= c.StartDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NenContract>> GetDueForCompletionAsync(
        DateTime asOf,
        CancellationToken cancellationToken = default)
    {
        // Mirrors NenContract.IsDueForCompletion(DateOnly) exactly.
        var asOfDate = DateOnly.FromDateTime(asOf);
        return await _context.NenContracts
            .Where(c => c.Status == NenContractStatus.Active && asOfDate > c.EndDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasContractsInEffectForPresetAsync(
        Guid contractPresetId,
        CancellationToken cancellationToken = default)
    {
        // Mirrors NenContract.IsInEffect exactly.
        return await _context.NenContracts
            .Where(c => c.ContractPresetId == contractPresetId)
            .Where(c => c.Status == NenContractStatus.Scheduled || c.Status == NenContractStatus.Active)
            .AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NenContract>> GetAllInEffectAsync(CancellationToken cancellationToken = default)
    {
        // Mirrors NenContract.IsInEffect exactly.
        return await _context.NenContracts
            .Where(c => c.Status == NenContractStatus.Scheduled || c.Status == NenContractStatus.Active)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NenContract>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.NenContracts.ToListAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var contract = await _context.NenContracts
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (contract is null) return;

        _context.NenContracts.Remove(contract);
    }
}
