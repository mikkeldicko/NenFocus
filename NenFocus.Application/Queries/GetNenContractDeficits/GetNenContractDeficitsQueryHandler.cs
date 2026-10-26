using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetNenContractDeficits;

/// <summary>Loads the contract and its blocks; the contract works out what is owed (Gap 106).</summary>
public sealed class GetNenContractDeficitsQueryHandler
    : IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>>
{
    private readonly INenContractRepository _contracts;
    private readonly IBlockRepository _blocks;
    private readonly ISystemClock _clock;

    public GetNenContractDeficitsQueryHandler(
        INenContractRepository contracts, IBlockRepository blocks, ISystemClock clock)
    {
        _contracts = contracts;
        _blocks = blocks;
        _clock = clock;
    }

    public async Task<IReadOnlyList<FocusBlockDeficitDto>> HandleAsync(
        GetNenContractDeficitsQuery query,
        CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(query.ContractId, ct);
        if (contract is null) return [];

        var blocks = await _blocks.GetForContractAsync(query.ContractId, ct);

        return contract.Deficits(blocks, _clock.Now)
            .Select(FocusBlockDeficitDto.From)
            .ToList();
    }
}
