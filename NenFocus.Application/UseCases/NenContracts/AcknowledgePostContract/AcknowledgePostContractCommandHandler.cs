using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.NenContracts.AcknowledgePostContract;

public sealed class AcknowledgePostContractCommandHandler
    : ICommandHandler<AcknowledgePostContractCommand, AcknowledgePostContractResult>
{
    private readonly INenContractRepository _contracts;
    private readonly ISystemClock _clock;

    public AcknowledgePostContractCommandHandler(INenContractRepository contracts, ISystemClock clock)
    {
        _contracts = contracts;
        _clock = clock;
    }

    public async Task<AcknowledgePostContractResult> HandleAsync(AcknowledgePostContractCommand command, CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(command.ContractId, ct);
        if (contract is null) return AcknowledgePostContractResult.Fail("Contract not found.");

        if (contract.Status != NenContractStatus.Completed && contract.Status != NenContractStatus.Broken)
            return AcknowledgePostContractResult.Fail("Post-contract can only be acknowledged for a completed or broken contract.");

        contract.MarkPostContractAcknowledged(_clock.Now);

        return AcknowledgePostContractResult.Ok();
    }
}
