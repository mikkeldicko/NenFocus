using NenFocus.Domain.ValueObjects;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;

namespace NenFocus.Application.Queries.GetNenContractCompletedFocusHours;

public sealed class GetNenContractCompletedFocusHoursQueryHandler
    : IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>
{
    private readonly INenContractRepository _contracts;
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISessionRepository _sessions;
    private readonly IFocusBlockOutcomeClassifier _classifier;
    private readonly IPlannedFocusHoursCalculator _calculator;
    private readonly ISystemClock _clock;

    public GetNenContractCompletedFocusHoursQueryHandler(
        INenContractRepository contracts,
        IPlannedBlockRepository blocks,
        ISessionRepository sessions,
        IFocusBlockOutcomeClassifier classifier,
        IPlannedFocusHoursCalculator calculator,
        ISystemClock clock)
    {
        _contracts = contracts;
        _blocks = blocks;
        _sessions = sessions;
        _classifier = classifier;
        _calculator = calculator;
        _clock = clock;
    }

    public async Task<IReadOnlyDictionary<DateOnly, double>> HandleAsync(
        GetNenContractCompletedFocusHoursQuery query,
        CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(query.ContractId, ct);
        if (contract is null)
            return new Dictionary<DateOnly, double>();

        var now = _clock.Now;
        var contractBlocks = await _blocks.GetForContractAsync(query.ContractId, ct);
        // Only the blocks that count toward the floor — a Bonus block's hours are Bonus hours.
        var focusBlocks = contractBlocks.OfType<FocusBlock>().Where(b => b.CountsTowardFloor).ToList();

        // Only Focus presets, not Background — a Spontaneous session's BlockPresetId can
        // only ever be a Focus preset (Ren only offers Focus-type presets), so matching
        // against Background presets too would silently never match anything; keeping the
        // set Focus-only makes that true by construction, not by coincidence.
        var focusPresetIds = focusBlocks.Select(b => b.BlockPresetId).ToHashSet();

        // Every credited window — from resolved blocks and matching spontaneous sessions
        // alike — local-converted and handed to the calculator once, so a window spanning
        // midnight (in either source) is split across the days it actually occupies exactly
        // like the planned-hours side already does.
        var creditedWindows = new List<TimeSlot>();

        foreach (var block in focusBlocks)
        {
            var session = await _sessions.GetByOriginatingBlockIdAsync(block.Id, ct);
            var actualElapsedMinutes = session?.GetElapsedTime(now)?.TotalMinutes ?? 0;
            var window = _classifier.CreditedWindow(
                block.Scheduled, block.RitualCompletedAt, session?.EndedAt, actualElapsedMinutes);

            if (window is { } w)
                creditedWindows.Add(w);
        }

        // Spontaneous sessions that started on one of the contract's local days — whole dates,
        // until Gap 67 stops counting them. The time zone says which real time those days are.
        var spontaneousSessions = await _sessions.GetHistoryAsync(
            _clock.TimeZone.SlotCovering(contract.StartDate, contract.EndDate), presetId: null, ct);
        foreach (var session in spontaneousSessions)
        {
            if (session.Origin != SessionOrigin.Spontaneous) continue;
            if (!focusPresetIds.Contains(session.BlockPresetId)) continue;
            if (session.StartedAt is not { } startedAt) continue;

            var minutes = session.GetElapsedTime(now)?.TotalMinutes ?? 0;
            if (minutes <= 0) continue;

            creditedWindows.Add(new TimeSlot(startedAt, startedAt + TimeSpan.FromMinutes(minutes)));
        }

        return _calculator.Calculate(creditedWindows, _clock.TimeZone);
    }
}
