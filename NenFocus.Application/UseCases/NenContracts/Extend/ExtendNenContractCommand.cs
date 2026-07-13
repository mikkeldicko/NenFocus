using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.Extend;

/// <summary>
/// Atomically pushes a contract's EndDate forward and creates every block scheduled for
/// the new days together — same atomicity reasoning as Seal. Whether the newly added days
/// need explicit floors is derived from the already-persisted contract's DailyFloorPolicy,
/// not re-specified here: a contract with a Default (Advanced) applies it automatically to
/// every new day except <see cref="RestDays"/>; a contract with no Default (Simple) derives
/// each new day's floor from that day's own planned Focus hours, same as at Seal.
/// </summary>
public sealed record ExtendNenContractCommand : ICommand<ExtendNenContractResult>
{
    public Guid ContractId { get; init; }
    public DateOnly NewEndDate { get; init; }

    /// <summary>Only meaningful for a contract whose DailyFloorPolicy has a Default (Advanced).</summary>
    public IReadOnlyList<DateOnly> RestDays { get; init; } = [];

    public IReadOnlyList<NenContractBlockSpec> Blocks { get; init; } = [];
}
