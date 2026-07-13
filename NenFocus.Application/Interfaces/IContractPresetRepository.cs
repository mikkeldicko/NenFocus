using NenFocus.Domain.Entities;

namespace NenFocus.Application.Interfaces;

public interface IContractPresetRepository
{
    Task<IReadOnlyList<ContractPreset>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ContractPreset?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(ContractPreset preset, CancellationToken cancellationToken = default);

    Task UpdateAsync(ContractPreset preset, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if a preset with the given name already exists. Used by validators
    /// to enforce unique preset names; excludeId omits the preset being updated from
    /// its own uniqueness check.
    /// </summary>
    Task<bool> ExistsWithNameAsync(
        string name,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);
}
