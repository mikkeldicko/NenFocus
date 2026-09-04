using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.CompleteNenContract;

public sealed class CompleteNenContractCommandHandler
    : ICommandHandler<CompleteNenContractCommand, CompleteNenContractResult>
{
    private readonly INenContractRepository _contracts;
    private readonly ISystemClock _clock;

    public CompleteNenContractCommandHandler(INenContractRepository contracts, ISystemClock clock)
    {
        _contracts = contracts;
        _clock = clock;
    }

    public async Task<CompleteNenContractResult> HandleAsync(CompleteNenContractCommand command, CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(command.ContractId, ct);
        if (contract is null) return CompleteNenContractResult.Fail("Contract not found.");

        // EndDate is deliberately left untouched — historically accurate ("sealed for 10
        // days, finished by day 7"), and NenContract.IsInEffect (Scheduled/Active only)
        // already governs the date-range-overlap check, so completing here frees the
        // calendar for a new contract immediately regardless of the old EndDate.
        contract.Complete(_clock.UtcNow);

        await _contracts.SaveAsync(contract, ct);
        return CompleteNenContractResult.Ok();
    }
}
