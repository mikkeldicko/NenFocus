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

        var now = _clock.UtcNow;
        var contractBlocks = await _blocks.GetForContractAsync(query.ContractId, ct);
        var focusBlocks = contractBlocks.OfType<FocusBlock>().ToList();

        // Only Focus presets, not Background — a Spontaneous session's BlockPresetId can
        // only ever be a Focus preset (Ren only offers Focus-type presets), so matching
        // against Background presets too would silently never match anything; keeping the
        // set Focus-only makes that true by construction, not by coincidence.
        var focusPresetIds = focusBlocks.Select(b => b.BlockPresetId).ToHashSet();

        // Every credited window — from resolved blocks and matching spontaneous sessions
        // alike — local-converted and handed to the calculator once, so a window spanning
        // midnight (in either source) is split across the days it actually occupies exactly
        // like the planned-hours side already does.
        var creditedWindows = new List<(DateTime Start, DateTime End)>();

        foreach (var block in focusBlocks)
        {
            var session = await _sessions.GetByOriginatingBlockIdAsync(block.Id, ct);
            var actualElapsedMinutes = session?.GetElapsedTime(now)?.TotalMinutes ?? 0;
            var window = _classifier.CreditedWindow(
                block.Start, block.End, block.RitualCompletedAt, session?.EndedAt, actualElapsedMinutes);

            if (window is { } w)
                creditedWindows.Add((_clock.ToLocal(w.Start), _clock.ToLocal(w.End)));
        }

        // The contract's dates are local, but GetHistoryAsync reads its dates as UTC days — so
        // fetch a day either side and decide on the session's own local date here, or a session
        // just after local midnight on the first day (still the previous UTC day) is missed and
        // one just after the last day (still its UTC day) is counted.
        var spontaneousSessions = await _sessions.GetHistoryAsync(
            contract.StartDate.AddDays(-1), contract.EndDate.AddDays(1), presetId: null, ct);
        foreach (var session in spontaneousSessions)
        {
            if (session.Origin != SessionOrigin.Spontaneous) continue;
            if (!focusPresetIds.Contains(session.BlockPresetId)) continue;
            if (session.StartedAt is not { } startedAt) continue;
            var localDate = _clock.ToLocalDate(startedAt);
            if (localDate < contract.StartDate || localDate > contract.EndDate) continue;

            var minutes = session.GetElapsedTime(now)?.TotalMinutes ?? 0;
            if (minutes <= 0) continue;

            creditedWindows.Add((_clock.ToLocal(startedAt), _clock.ToLocal(startedAt.AddMinutes(minutes))));
        }

        return _calculator.Calculate(creditedWindows);
    }
}
