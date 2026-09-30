using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.GetPlannedBlocksForDay;

public sealed class GetPlannedBlocksForDayQueryHandler
    : IQueryHandler<GetPlannedBlocksForDayQuery, IReadOnlyList<PlannedBlockDto>>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly INenContractRepository _contracts;
    private readonly ISystemClock _clock;
    private readonly IFocusBlockOutcomeClassifier _classifier;

    public GetPlannedBlocksForDayQueryHandler(
        IPlannedBlockRepository blocks,
        IBlockPresetRepository presets,
        INenContractRepository contracts,
        ISystemClock clock,
        IFocusBlockOutcomeClassifier classifier)
    {
        _blocks = blocks;
        _presets = presets;
        _contracts = contracts;
        _clock = clock;
        _classifier = classifier;
    }

    public async Task<IReadOnlyList<PlannedBlockDto>> HandleAsync(
        GetPlannedBlocksForDayQuery query,
        CancellationToken cancellationToken = default)
    {
        var blocks = (await _blocks.GetForDayAsync(query.Date, cancellationToken))
            .Where(b => !b.CancelledAt.HasValue)
            .ToList();

        var allPresets = await _presets.GetAllAsync(cancellationToken);
        var presetMap = allPresets.ToDictionary(p => p.Id);
        var now = _clock.Now;

        var contractIds = blocks.Select(b => b.ContractId).Where(id => id.HasValue).Select(id => id!.Value).Distinct();
        var contractsById = new Dictionary<Guid, NenContract?>();
        foreach (var contractId in contractIds)
            contractsById[contractId] = await _contracts.GetByIdAsync(contractId, cancellationToken);

        // Bonus resolution needs every contract regardless of status, not just the ones
        // this day's blocks already link to — a block's Start date can fall inside a
        // contract's range without ever being committed to it.
        var allContracts = await _contracts.GetAllAsync(cancellationToken);

        return blocks.Select(b =>
        {
            var preset = presetMap.TryGetValue(b.BlockPresetId, out var p) ? p : null;
            var focusBlock = b as FocusBlock;

            // wasStartedLate is independent of Outcome — EndedEarly takes priority over
            // StartedLate in Classify (a block can only carry one verdict), but a block can
            // genuinely be both. This flag lets the tile badge say so even when Outcome alone
            // would hide it.
            var wasStartedLate = focusBlock?.RitualCompletedAt is { } ritualCompletedAt
                && _classifier.IsLateStart(b.Scheduled, ritualCompletedAt)
                && !b.Scheduled.HasEnded(ritualCompletedAt);

            // Display span mirrors the actual worked window, not the scheduled one: a ritual
            // completed past the grace period pulls the visual start forward, and a session
            // ended before the block's own scheduled end pulls the visual end back. A grace-
            // period-late start is left untrimmed — it's credited as full duration, so it
            // should look like full duration too.
            var shown = wasStartedLate ? b.Scheduled.From(focusBlock!.RitualCompletedAt!.Value) : b.Scheduled;
            if (focusBlock?.SessionEndedAt is { } sessionEndedAt && !b.Scheduled.HasEnded(sessionEndedAt))
                shown = shown.Until(sessionEndedAt);

            // Bonus is only meaningful for an unlinked block — a committed or redeemed block
            // (ContractId set) already has its one true contract. Contract date ranges never
            // overlap, so at most one contract can ever cover a given Start date. StartsAt/
            // EndsAt, not a date-only truncation — a Work-Hours contract's true boundary
            // isn't midnight. StartsAt/EndsAt are local wall-clock times, so the block's start
            // is compared on the user's clock.
            var localStart = _clock.TimeZone.ToCalendarTimes(b.Scheduled).Start;
            // Focus only — Bonus hours only ever come from Focus blocks (same set as
            // GetNenContractBonusFocusHoursQueryHandler); a Background block is never Bonus.
            var bonusContract = b is FocusBlock && b.ContractId is null
                ? allContracts.FirstOrDefault(c => c.Covers(localStart))
                : null;

            // Redemption and Bonus are mutually exclusive by construction (Redemption always
            // has ContractId set; Bonus never does) — Redemption is checked first only because
            // it's the linked case, not because of any real priority conflict between the two.
            var contractBadge = focusBlock?.IsRedemption == true
                ? PlannedBlockContractBadge.Redemption
                : bonusContract is not null
                    ? PlannedBlockContractBadge.Bonus
                    : (PlannedBlockContractBadge?)null;

            return new PlannedBlockDto
            {
                Id = b.Id,
                PresetId = b.BlockPresetId,
                PresetName = preset?.Name ?? "Unknown",
                BlockPresetType = b.PresetType,
                Shown = shown,
                WebsiteFilterType = preset?.WebsiteFilterType ?? WebsiteFilterType.Blocklist,
                ContractId = b.ContractId,
                IsCompleted = focusBlock?.RitualCompletedAt.HasValue ?? false,
                HasEnded = b.HasEnded(now),
                HasStarted = b.HasStarted(now),
                IsUnderInEffectContract = b.ContractId.HasValue && (contractsById.GetValueOrDefault(b.ContractId.Value)?.IsInEffect ?? false),
                ContractStrictnessRule = b.ContractId.HasValue ? contractsById.GetValueOrDefault(b.ContractId.Value)?.StrictnessRule : null,
                Outcome = focusBlock is not null
                    ? _classifier.Classify(focusBlock, now)
                    : null,
                WasStartedLate = wasStartedLate,
                ContractBadge = contractBadge,
                BonusContractName = bonusContract?.Name
            };
        }).ToList();
    }
}
