using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;
using NenFocus.Application.Queries.GetNenContractDayStatuses;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Queries.GetNenContractRecap;

public sealed class GetNenContractRecapQueryHandler : IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?>
{
    private readonly INenContractRepository _contracts;
    private readonly IBlockRepository _blocks;
    private readonly IQueryHandler<GetNenContractDayStatusesQuery, IReadOnlyList<ContractDayStatus>> _dayStatuses;
    private readonly ISystemClock _clock;

    public GetNenContractRecapQueryHandler(
        INenContractRepository contracts,
        IBlockRepository blocks,
        IQueryHandler<GetNenContractDayStatusesQuery, IReadOnlyList<ContractDayStatus>> dayStatuses,
        ISystemClock clock)
    {
        _contracts = contracts;
        _blocks = blocks;
        _dayStatuses = dayStatuses;
        _clock = clock;
    }

    public async Task<NenContractRecapDto?> HandleAsync(GetNenContractRecapQuery query, CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(query.ContractId, ct);
        if (contract is null) return null;

        var now = _clock.Now;

        var linkedBlocks = await _blocks.GetForContractAsync(query.ContractId, ct);

        var totalFocusBlocks = 0;
        var totalBackgroundBlocks = 0;
        var redemptionBlocks = 0;
        var completedOnTime = 0;
        var startedLate = 0;
        var endedEarly = 0;
        var missed = 0;
        var cancelled = 0;
        var bonusBlockCount = 0;

        foreach (var block in linkedBlocks)
        {
            // Bonus blocks are counted apart from the contract's own (Gap 97): never in the
            // totals or outcomes, and a removed (cancelled) one not at all.
            if (block.Membership is { IsBonus: true })
            {
                if (block is FocusBlock && !block.CancelledAt.HasValue) bonusBlockCount++;
                continue;
            }

            if (block is BackgroundBlock)
            {
                totalBackgroundBlocks++;
                if (block.CancelledAt.HasValue) cancelled++;
                continue;
            }

            if (block is not FocusBlock focusBlock) continue;
            totalFocusBlocks++;

            if (focusBlock.IsRedemption) redemptionBlocks++;

            // Cancelled is reported as its own category, never asked its outcome — same
            // split GetNenContractRecapQuery was originally designed around (Open Gap G).
            if (focusBlock.CancelledAt.HasValue)
            {
                cancelled++;
                continue;
            }

            var outcome = focusBlock.Outcome(now);

            switch (outcome)
            {
                case FocusBlockOutcome.CompletedOnTime: completedOnTime++; break;
                case FocusBlockOutcome.StartedLate: startedLate++; break;
                case FocusBlockOutcome.EndedEarly: endedEarly++; break;
                case FocusBlockOutcome.Missed: missed++; break;
            }
        }

        // The one day rule (NenContract.DayStatuses, Gap 95) — the same days the Planner shows.
        var days = await _dayStatuses.HandleAsync(new GetNenContractDayStatusesQuery(query.ContractId), ct);

        var totalCompletedHours = days.Sum(d => d.CompletedHours);
        var totalBonusHours = days.Sum(d => d.BonusHours);

        var dailyBreakdown = new List<NenContractDailyRecapEntryDto>();
        var bestStreakDays = 0;
        var currentStreakDays = 0;
        foreach (var day in days)
        {
            dailyBreakdown.Add(new NenContractDailyRecapEntryDto
            {
                Date = day.Date,
                IsRestDay = day.IsRest,
                Percentage = day.PercentMet
            });

            // Rest days are skipped entirely — they neither extend nor break a streak.
            if (day.IsRest) continue;

            if (day.IsMet)
            {
                currentStreakDays++;
                bestStreakDays = Math.Max(bestStreakDays, currentStreakDays);
            }
            else
            {
                currentStreakDays = 0;
            }
        }

        double? completionPercentage = contract.CommittedFocusHours > 0
            ? totalCompletedHours / contract.CommittedFocusHours
            : null;

        string? completionGrade = completionPercentage switch
        {
            null => null,
            >= 1.0 => "Completed",
            <= 0.0 => "Failed",
            _ => "Partially Completed"
        };

        return new NenContractRecapDto
        {
            ContractId = contract.Id,
            Name = contract.Name,
            Status = contract.Status,
            StartDate = contract.StartDate,
            EndDate = contract.EndDate,
            StrictnessRule = contract.StrictnessRule,
            CommittedFocusHours = contract.CommittedFocusHours,
            TotalCompletedHours = totalCompletedHours,
            TotalBonusHours = totalBonusHours,
            CompletionPercentage = completionPercentage,
            CompletionGrade = completionGrade,
            TotalFocusBlocks = totalFocusBlocks,
            TotalBackgroundBlocks = totalBackgroundBlocks,
            RedemptionBlocks = redemptionBlocks,
            CompletedOnTimeBlocks = completedOnTime,
            StartedLateBlocks = startedLate,
            EndedEarlyBlocks = endedEarly,
            MissedBlocks = missed,
            CancelledBlocks = cancelled,
            BonusBlockCount = bonusBlockCount,
            DailyBreakdown = dailyBreakdown,
            BestStreakDays = bestStreakDays,
            Reflection = contract.Reflection
        };
    }
}
