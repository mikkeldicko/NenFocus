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
    private readonly ISystemClock _clock;

    public GetNenContractCompletedFocusHoursQueryHandler(
        INenContractRepository contracts,
        IPlannedBlockRepository blocks,
        ISessionRepository sessions,
        IFocusBlockOutcomeClassifier classifier,
        ISystemClock clock)
    {
        _contracts = contracts;
        _blocks = blocks;
        _sessions = sessions;
        _classifier = classifier;
        _clock = clock;
    }

    public async Task<IReadOnlyDictionary<DateOnly, double>> HandleAsync(
        GetNenContractCompletedFocusHoursQuery query,
        CancellationToken ct = default)
    {
        var hoursByDay = new Dictionary<DateOnly, double>();

        var contract = await _contracts.GetByIdAsync(query.ContractId, ct);
        if (contract is null)
            return hoursByDay;

        var now = _clock.UtcNow;
        var contractBlocks = await _blocks.GetForContractAsync(query.ContractId, ct);
        var focusBlocks = contractBlocks.OfType<FocusBlock>().ToList();

        // Only Focus presets, not Background — a Spontaneous session's BlockPresetId can
        // only ever be a Focus preset (Ren only offers Focus-type presets), so matching
        // against Background presets too would silently never match anything; keeping the
        // set Focus-only makes that true by construction, not by coincidence.
        var focusPresetIds = focusBlocks.Select(b => b.BlockPresetId).ToHashSet();

        foreach (var block in focusBlocks)
        {
            var outcome = _classifier.Classify(block.Start, block.End, block.RitualCompletedAt, now);
            if (outcome is null)
                continue; // still open — nothing resolved to credit yet

            var session = await _sessions.GetByOriginatingBlockIdAsync(block.Id, ct);
            var actualElapsedMinutes = session?.GetElapsedTime(now)?.TotalMinutes ?? 0;
            var creditedMinutes = _classifier.CreditedFocusMinutes(
                block.Start, block.End, block.RitualCompletedAt, actualElapsedMinutes);

            AddMinutes(hoursByDay, DateOnly.FromDateTime(block.Start), creditedMinutes);
        }

        var spontaneousSessions = await _sessions.GetHistoryAsync(contract.StartDate, contract.EndDate, presetId: null, ct);
        foreach (var session in spontaneousSessions)
        {
            if (session.Origin != SessionOrigin.Spontaneous) continue;
            if (!focusPresetIds.Contains(session.BlockPresetId)) continue;
            if (session.StartedAt is not { } startedAt) continue;

            var minutes = session.GetElapsedTime(now)?.TotalMinutes ?? 0;
            AddMinutes(hoursByDay, DateOnly.FromDateTime(startedAt), minutes);
        }

        return hoursByDay;
    }

    private static void AddMinutes(Dictionary<DateOnly, double> hoursByDay, DateOnly date, double minutes)
    {
        if (minutes <= 0) return;
        hoursByDay[date] = hoursByDay.GetValueOrDefault(date) + minutes / 60.0;
    }
}
