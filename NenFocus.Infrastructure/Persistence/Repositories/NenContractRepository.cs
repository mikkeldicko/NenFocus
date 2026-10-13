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

    public async Task<bool> HasContractsInEffectForPresetAsync(
        Guid contractPresetId,
        CancellationToken cancellationToken = default)
    {
        // Fetch, and the entity decides (MODULE_STANDARDS → Repositories): contracts are few,
        // so there is no SQL copy of IsInEffect to drift (Gap 101).
        var contracts = await _context.NenContracts
            .Where(c => c.ContractPresetId == contractPresetId)
            .ToListAsync(cancellationToken);
        return contracts.Any(c => c.IsInEffect);
    }

    public async Task<IReadOnlyList<NenContract>> GetAllInEffectAsync(CancellationToken cancellationToken = default)
    {
        // Fetch, and the entity decides — see above.
        var contracts = await _context.NenContracts.ToListAsync(cancellationToken);
        return contracts.Where(c => c.IsInEffect).ToList();
    }

    public async Task<IReadOnlyList<NenContract>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.NenContracts.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NenContract>> GetUnacknowledgedPostContractsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.NenContracts
            .Where(c =>
                (c.Status == NenContractStatus.Completed || c.Status == NenContractStatus.Broken) &&
                c.PostContractAcknowledgedAt == null)
            .OrderBy(c => c.EndDate)
            .ToListAsync(cancellationToken);
    }

    public void Add(NenContract contract) => _context.NenContracts.Add(contract);

    public void Remove(NenContract contract) => _context.NenContracts.Remove(contract);
}
