using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.AcknowledgePostContract;

public sealed record AcknowledgePostContractCommand : ICommand<AcknowledgePostContractResult>
{
    public Guid ContractId { get; init; }
}
