using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Services;

namespace NenFocus.Application.Queries.GetNenContractBonusFocusHours;

public sealed class GetNenContractBonusFocusHoursQueryHandler
    : IQueryHandler<GetNenContractBonusFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>
{
    private readonly INenContractRepository _contracts;
    private readonly IPlannedBlockRepository _blocks;
    private readonly IPlannedFocusHoursCalculator _calculator;
    private readonly ISystemClock _clock;

    public GetNenContractBonusFocusHoursQueryHandler(
        INenContractRepository contracts,
        IPlannedBlockRepository blocks,
        IPlannedFocusHoursCalculator calculator,
        ISystemClock clock)
    {
        _contracts = contracts;
        _blocks = blocks;
        _calculator = calculator;
        _clock = clock;
    }

    public async Task<IReadOnlyDictionary<DateOnly, double>> HandleAsync(
        GetNenContractBonusFocusHoursQuery query,
        CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(query.ContractId, ct);
        if (contract is null)
            return new Dictionary<DateOnly, double>();

        var now = _clock.Now;

        // The contract's Bonus blocks are the ones that say so themselves (Gap 97) — the role is
        // stored when the block is placed, never worked out here from dates.
        var bonusBlocks = (await _blocks.GetForContractAsync(contract.Id, ct))
            .OfType<FocusBlock>()
            .Where(b => b.IsBonus)
            .ToList();

        var creditedWindows = new List<TimeSlot>();
        foreach (var block in bonusBlocks)
        {
            if (block.CreditedSlot(now) is { } credited)
                creditedWindows.Add(credited);
        }

        return _calculator.Calculate(creditedWindows, _clock.TimeZone);
    }
}
