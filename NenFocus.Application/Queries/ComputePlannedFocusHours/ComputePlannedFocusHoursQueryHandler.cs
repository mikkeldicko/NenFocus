using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Services;

namespace NenFocus.Application.Queries.ComputePlannedFocusHours;

public sealed class ComputePlannedFocusHoursQueryHandler
    : IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IPlannedFocusHoursCalculator _calculator;
    private readonly ISystemClock _clock;

    public ComputePlannedFocusHoursQueryHandler(IPlannedBlockRepository blocks, IPlannedFocusHoursCalculator calculator, ISystemClock clock)
    {
        _blocks = blocks;
        _calculator = calculator;
        _clock = clock;
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

        // Calculator buckets by calendar day off whatever it's given — convert to local
        // here so "which day" always means the user's local day, not the UTC day these
        // ranges are stored/passed in as.
        var localRanges = ranges.Select(r => (_clock.ToLocal(r.Start), _clock.ToLocal(r.End)));

        return _calculator.Calculate(localRanges);
    }
}
