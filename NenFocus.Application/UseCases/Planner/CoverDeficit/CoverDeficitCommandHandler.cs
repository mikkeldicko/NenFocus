using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.CoverDeficit;

/// <summary>
/// The validator has allowed the placement; the contract builds the block (<c>PlaceRedemptionBlock</c>,
/// which runs the same check to work out the receipt and capped slot) and it is saved — one block,
/// one transaction (Gap 106).
/// </summary>
public sealed class CoverDeficitCommandHandler : ICommandHandler<CoverDeficitCommand, CoverDeficitResult>
{
    private readonly INenContractRepository _contracts;
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISystemClock _clock;

    public CoverDeficitCommandHandler(INenContractRepository contracts, IPlannedBlockRepository blocks, ISystemClock clock)
    {
        _contracts = contracts;
        _blocks = blocks;
        _clock = clock;
    }

    public async Task<CoverDeficitResult> HandleAsync(CoverDeficitCommand command, CancellationToken cancellationToken = default)
    {
        var contract = await _contracts.GetByIdAsync(command.ContractId, cancellationToken);
        if (contract is null) return CoverDeficitResult.Fail(RedemptionVerdictDto.ContractNotFound);

        var now = _clock.Now;
        var contractBlocks = await _blocks.GetForContractAsync(contract.Id, cancellationToken);
        var verdict = contract.CheckRedemptionPlacement(command.PresetId, command.Slot, contractBlocks, now, _clock.TimeZone);
        var block = contract.PlaceRedemptionBlock(Guid.NewGuid(), command.PresetId, command.Slot, contractBlocks, now, _clock.TimeZone);

        await _blocks.AddAsync(block, cancellationToken);
        return CoverDeficitResult.Ok(block.Id, verdict.WasCapped);
    }
}
