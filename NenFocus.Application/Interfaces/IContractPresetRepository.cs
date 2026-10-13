using NenFocus.Domain.Entities;

namespace NenFocus.Application.Interfaces;

public interface IContractPresetRepository
{
    Task<IReadOnlyList<ContractPreset>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ContractPreset?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if a preset with the given name already exists. Used by validators
    /// to enforce unique preset names; excludeId omits the preset being updated from
    /// its own uniqueness check.
    /// </summary>
    Task<bool> ExistsWithNameAsync(
        string name,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new contract preset; it is saved when the command commits (Gap 110).</summary>
    void Add(ContractPreset preset);

    /// <summary>Removes a loaded contract preset; saved when the command commits. Ask first whether it may go.</summary>
    void Remove(ContractPreset preset);
}
