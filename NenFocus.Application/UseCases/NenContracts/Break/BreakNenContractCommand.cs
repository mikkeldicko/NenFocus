using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.Break;

/// <summary>
/// Breaks an active contract and atomically deletes every one of its linked blocks that
/// hasn't fully ended yet — one command, one transaction, no partial "broken but blocks
/// unresolved" state can ever be persisted. Blocks that already ended stay untouched.
/// </summary>
public sealed record BreakNenContractCommand : ICommand<BreakNenContractResult>
{
    public Guid ContractId { get; init; }
}
