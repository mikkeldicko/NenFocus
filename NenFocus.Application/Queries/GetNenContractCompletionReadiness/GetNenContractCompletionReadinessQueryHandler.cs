using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;
using NenFocus.Application.Queries.GetNenContractCompletedFocusHours;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;

namespace NenFocus.Application.Queries.GetNenContractCompletionReadiness;

public sealed class GetNenContractCompletionReadinessQueryHandler
    : IQueryHandler<GetNenContractCompletionReadinessQuery, NenContractCompletionReadinessDto>
{
    private readonly INenContractRepository _contracts;
    private readonly IPlannedBlockRepository _blocks;
    private readonly IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _completedHours;
    private readonly ISystemClock _clock;
    private readonly IFocusBlockOutcomeClassifier _outcomeClassifier;

    public GetNenContractCompletionReadinessQueryHandler(
        INenContractRepository contracts,
        IPlannedBlockRepository blocks,
        IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> completedHours,
        ISystemClock clock,
        IFocusBlockOutcomeClassifier outcomeClassifier)
    {
        _contracts = contracts;
        _blocks = blocks;
        _completedHours = completedHours;
        _clock = clock;
        _outcomeClassifier = outcomeClassifier;
    }

    private static readonly NenContractCompletionReadinessDto NotReady = new()
    {
        IsReady = false,
        RemainingHours = 0,
        PendingBlockCount = 0
    };

    public async Task<NenContractCompletionReadinessDto> HandleAsync(
        GetNenContractCompletionReadinessQuery query, CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(query.ContractId, ct);
        if (contract is null || contract.Status != NenContractStatus.Active)
            return NotReady;

        var completedByDay = await _completedHours.HandleAsync(
            new GetNenContractCompletedFocusHoursQuery(contract.Id), ct);
        var totalCompleted = completedByDay.Values.Sum();
        var remainingHours = Math.Max(0, contract.CommittedFocusHours - totalCompleted);

        var now = _clock.Now;
        // Every member, Bonus blocks included (Gap 97): the contract can't complete early while
        // any block of its own is unfinished.
        // IsResolved (not raw HasEnded) so a FocusBlock whose session ended early doesn't
        // keep blocking readiness until its original scheduled End arrives — see
        // PlannedBlockResolutionExtensions.
        var members = await _blocks.GetForContractAsync(contract.Id, ct);
        var pendingBlockCount = members.Count(b =>
            !b.CancelledAt.HasValue &&
            !b.IsResolved(_outcomeClassifier, now));

        return new NenContractCompletionReadinessDto
        {
            IsReady = remainingHours <= 0 && pendingBlockCount == 0,
            RemainingHours = remainingHours,
            PendingBlockCount = pendingBlockCount
        };
    }
}
