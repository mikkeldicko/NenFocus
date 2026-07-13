using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Services;

namespace NenFocus.Application.Queries.ComputePlannedFocusHours;

public sealed class ComputePlannedFocusHoursQueryHandler
    : IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IPlannedFocusHoursCalculator _calculator;

    public ComputePlannedFocusHoursQueryHandler(IPlannedBlockRepository blocks, IPlannedFocusHoursCalculator calculator)
    {
        _blocks = blocks;
        _calculator = calculator;
    }

    public async Task<IReadOnlyDictionary<DateOnly, double>> HandleAsync(
        ComputePlannedFocusHoursQuery query,
        CancellationToken ct = default)
    {
        var ranges = new List<(DateTime Start, DateTime End)>();

        if (query.ContractId.HasValue)
        {
            var persisted = await _blocks.GetForContractAsync(query.ContractId.Value, ct);
            ranges.AddRange(persisted
                .OfType<FocusBlock>()
                .Where(b => b.Id != query.ExcludeBlockId)
                .Select(b => (b.Start, b.End)));
        }

        ranges.AddRange(query.DraftFocusBlockRanges);

        return _calculator.Calculate(ranges);
    }
}
