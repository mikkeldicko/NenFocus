using Microsoft.EntityFrameworkCore;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Infrastructure.Persistence.Repositories;

public sealed class ContractPresetRepository : IContractPresetRepository
{
    private readonly NenFocusDbContext _context;

    public ContractPresetRepository(NenFocusDbContext context)
        => _context = context;

    public async Task<IReadOnlyList<ContractPreset>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.ContractPresets
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<ContractPreset?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.ContractPresets
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<bool> ExistsWithNameAsync(
        string name,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ContractPresets
            .Where(p => p.Name == name.Trim()); // Name is NOCASE

        if (excludeId.HasValue)
            query = query.Where(p => p.Id != excludeId.Value);

        return await query.AnyAsync(cancellationToken);
    }

    public void Add(ContractPreset preset) => _context.ContractPresets.Add(preset);

    public void Remove(ContractPreset preset) => _context.ContractPresets.Remove(preset);
}
