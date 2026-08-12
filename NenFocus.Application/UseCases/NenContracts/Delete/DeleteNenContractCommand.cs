using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.Delete;

/// <summary>
/// Deletes a Scheduled (not yet started) contract outright, along with every block linked
/// to it — the blocks it drafted never ran, so there's no historical record to preserve.
/// Only ever valid for Status == Scheduled; an Active contract is cancelled via
/// BreakNenContractCommand instead, which deliberately preserves already-ended blocks.
/// </summary>
public sealed record DeleteNenContractCommand : ICommand<DeleteNenContractResult>
{
    public Guid ContractId { get; init; }
}
