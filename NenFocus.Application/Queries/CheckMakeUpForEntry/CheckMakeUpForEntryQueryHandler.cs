using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Queries.CheckMakeUpForEntry;

public sealed class CheckMakeUpForEntryQueryHandler : IQueryHandler<CheckMakeUpForEntryQuery, MakeUpForEntryCheckResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly INenContractRepository _contracts;
    private readonly IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> _deficits;

    public CheckMakeUpForEntryQueryHandler(
        IPlannedBlockRepository blocks,
        INenContractRepository contracts,
        IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> deficits)
    {
        _blocks = blocks;
        _contracts = contracts;
        _deficits = deficits;
    }

    public async Task<MakeUpForEntryCheckResult> HandleAsync(CheckMakeUpForEntryQuery query, CancellationToken ct = default)
    {
        var missedBlock = await _blocks.GetByIdAsync(query.MissedBlockId, ct) as FocusBlock;
        if (missedBlock is null || missedBlock.ContractId is not { } contractId)
            return MakeUpForEntryCheckResult.MissedBlockNotFound;

        if (missedBlock.BlockPresetId != query.PresetId)
            return MakeUpForEntryCheckResult.PresetMismatch;

        var contract = await _contracts.GetByIdAsync(contractId, ct);
        if (contract is null)
            return MakeUpForEntryCheckResult.MissedBlockNotFound;

        var eligibility = contract.CheckMakeUpEligibility(
            DateOnly.FromDateTime(missedBlock.Start),
            DateOnly.FromDateTime(query.BlockDate));

        switch (eligibility)
        {
            case MakeUpEligibility.NotAllowed:
                return MakeUpForEntryCheckResult.StrictnessDoesNotAllowMakeUp;
            case MakeUpEligibility.WrongDay:
                return MakeUpForEntryCheckResult.WrongDay;
        }

        var deficits = await _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), ct);
        var remainingDeficit = deficits.FirstOrDefault(d => d.FocusBlockId == query.MissedBlockId)?.RemainingDeficitMinutes ?? 0;

        if (query.ExcludeBlockId is { } excludeId)
        {
            var editingBlock = await _blocks.GetByIdAsync(excludeId, ct) as FocusBlock;
            var priorContribution = editingBlock?.MakeUpForEntries
                .Where(e => e.MissedBlockId == query.MissedBlockId)
                .Sum(e => e.MinutesCovered) ?? 0;
            remainingDeficit += priorContribution;
        }

        return query.MinutesCovered > remainingDeficit
            ? MakeUpForEntryCheckResult.ExceedsRemainingDeficit
            : MakeUpForEntryCheckResult.Valid;
    }
}
