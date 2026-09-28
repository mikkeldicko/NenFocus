using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractBonusFocusHours;
using NenFocus.Application.Queries.GetNenContractCompletedFocusHours;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;

namespace NenFocus.Application.Queries.GetNenContractRecap;

public sealed class GetNenContractRecapQueryHandler : IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?>
{
    private readonly INenContractRepository _contracts;
    private readonly IPlannedBlockRepository _blocks;
    private readonly IFocusBlockOutcomeClassifier _classifier;
    private readonly IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _completedHours;
    private readonly IQueryHandler<GetNenContractBonusFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _bonusHours;
    private readonly ISystemClock _clock;

    public GetNenContractRecapQueryHandler(
        INenContractRepository contracts,
        IPlannedBlockRepository blocks,
        IFocusBlockOutcomeClassifier classifier,
        IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> completedHours,
        IQueryHandler<GetNenContractBonusFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> bonusHours,
        ISystemClock clock)
    {
        _contracts = contracts;
        _blocks = blocks;
        _classifier = classifier;
        _completedHours = completedHours;
        _bonusHours = bonusHours;
        _clock = clock;
    }

    public async Task<NenContractRecapDto?> HandleAsync(GetNenContractRecapQuery query, CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(query.ContractId, ct);
        if (contract is null) return null;

        var now = _clock.UtcNow;

        var linkedBlocks = await _blocks.GetForContractAsync(query.ContractId, ct);

        var totalFocusBlocks = 0;
        var totalBackgroundBlocks = 0;
        var redemptionBlocks = 0;
        var completedOnTime = 0;
        var startedLate = 0;
        var endedEarly = 0;
        var missed = 0;
        var cancelled = 0;

        foreach (var block in linkedBlocks)
        {
            if (block is BackgroundBlock)
            {
                totalBackgroundBlocks++;
                if (block.CancelledAt.HasValue) cancelled++;
                continue;
            }

            if (block is not FocusBlock focusBlock) continue;
            totalFocusBlocks++;

            if (focusBlock.IsRedemption) redemptionBlocks++;

            // Cancelled is reported as its own category, never run through Classify — same
            // split GetNenContractRecapQuery was originally designed around (Open Gap G).
            if (focusBlock.CancelledAt.HasValue)
            {
                cancelled++;
                continue;
            }

            var outcome = _classifier.Classify(focusBlock, now);

            switch (outcome)
            {
                case FocusBlockOutcome.CompletedOnTime: completedOnTime++; break;
                case FocusBlockOutcome.StartedLate: startedLate++; break;
                case FocusBlockOutcome.EndedEarly: endedEarly++; break;
                case FocusBlockOutcome.Missed: missed++; break;
            }
        }

        var completedHoursByDay = await _completedHours.HandleAsync(new GetNenContractCompletedFocusHoursQuery(query.ContractId), ct);
        var bonusHoursByDay = await _bonusHours.HandleAsync(new GetNenContractBonusFocusHoursQuery(query.ContractId), ct);

        var totalCompletedHours = completedHoursByDay.Values.Sum();
        var totalBonusHours = bonusHoursByDay.Values.Sum();

        // Bonus block count, mirroring GetNenContractBonusFocusHoursQueryHandler's own
        // block-sourcing exactly, so "how many Bonus blocks" and "how many Bonus hours"
        // are always answered from the same underlying set.
        var rangeStart = contract.StartDate.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var rangeEnd = contract.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var blocksInRange = await _blocks.GetForRangeAsync(rangeStart, rangeEnd, ct);
        var bonusBlockCount = blocksInRange.Count(b =>
            b.ContractId is null &&
            contract.Covers(_clock.ToLocal(b.Start)));

        var dailyBreakdown = new List<NenContractDailyRecapEntryDto>();
        var bestStreakDays = 0;
        var currentStreakDays = 0;
        for (var date = contract.StartDate; date <= contract.EndDate; date = date.AddDays(1))
        {
            var completedThatDay = completedHoursByDay.GetValueOrDefault(date);
            var isRestDay = contract.IsRestDay(date);

            dailyBreakdown.Add(new NenContractDailyRecapEntryDto
            {
                Date = date,
                IsRestDay = isRestDay,
                Percentage = contract.PercentOfFloorMet(date, completedThatDay)
            });

            // Rest days are skipped entirely — they neither extend nor break a streak.
            if (isRestDay) continue;

            if (contract.IsDayMet(date, completedThatDay))
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
