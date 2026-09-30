using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Planner.SplitPlannedBlock;

public sealed class SplitPlannedBlockCommandHandler : ICommandHandler<SplitPlannedBlockCommand, SplitPlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> _computeRedemptionAttribution;

    public SplitPlannedBlockCommandHandler(
        IPlannedBlockRepository blocks,
        IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> computeRedemptionAttribution)
    {
        _blocks = blocks;
        _computeRedemptionAttribution = computeRedemptionAttribution;
    }

    public async Task<SplitPlannedBlockResult> HandleAsync(SplitPlannedBlockCommand command, CancellationToken cancellationToken = default)
    {
        var original = await _blocks.GetByIdAsync(command.OriginalBlockId, cancellationToken);
        if (original is null)
            return SplitPlannedBlockResult.Fail($"Planned block '{command.OriginalBlockId}' not found.");

        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        PlannedBlock first;
        PlannedBlock second;

        if (original is FocusBlock focusOriginal)
        {
            // Split in half, not two independent creations: querying attribution once for
            // the whole span of both halves and then
            // partitioning the result by each half's own duration keeps the total minutes
            // covered identical to what the un-split block would have covered — computing
            // each half separately would have both halves independently re-claim against
            // the same still-unpersisted deficit and double-count it.
            var attribution = focusOriginal.ContractId is { } contractId
                ? await _computeRedemptionAttribution.HandleAsync(
                    new ComputeRedemptionAttributionQuery(
                        contractId, focusOriginal.BlockPresetId, TimeSlot.Enclosing(command.First, command.Second), original.Id),
                    cancellationToken)
                : new RedemptionAttributionResult(null, [], TimeSlot.Enclosing(command.First, command.Second));

            var firstDurationMinutes = command.First.Length.TotalMinutes;
            var (firstEntries, secondEntries) = SplitEntriesAcrossHalves(attribution.Entries, firstDurationMinutes);

            // A half can still end up needing its own cap even though the combined span's
            // entries were queried once for the whole original window: if the original
            // covered less than its own full duration, both resulting halves inherit that
            // same shortfall proportionally, and each must independently respect "duration
            // never exceeds what it covers" — the same rule Create/Update enforce.
            var firstSlot = FocusBlock.CapEndToRedemptionCoverage(command.First, firstEntries);
            var secondSlot = FocusBlock.CapEndToRedemptionCoverage(command.Second, secondEntries);

            first = FocusBlock.Create(
                firstId, focusOriginal.BlockPresetId, firstSlot,
                focusOriginal.Title, attribution.ContractId, firstEntries);
            second = FocusBlock.Create(
                secondId, focusOriginal.BlockPresetId, secondSlot,
                focusOriginal.Title, attribution.ContractId, secondEntries);
        }
        else
        {
            first = BackgroundBlock.Create(
                firstId, original.BlockPresetId, command.First,
                original.Title, original.ContractId);
            second = BackgroundBlock.Create(
                secondId, original.BlockPresetId, command.Second,
                original.Title, original.ContractId);
        }

        await _blocks.DeleteAsync(original.Id, cancellationToken);
        await _blocks.AddAsync(first, cancellationToken);
        await _blocks.AddAsync(second, cancellationToken);

        return SplitPlannedBlockResult.Ok(firstId, secondId);
    }

    /// <summary>
    /// Distributes a combined-span entries list across two chronologically-ordered halves:
    /// an entry entirely within the first half's own duration goes to the first half, one
    /// entirely beyond it goes to the second, and one straddling the boundary is split into
    /// two entries against the same missed block, one per half.
    /// </summary>
    private static (List<RedemptionEntry> First, List<RedemptionEntry> Second) SplitEntriesAcrossHalves(
        IReadOnlyList<RedemptionEntryDto> entries, double firstDurationMinutes)
    {
        var first = new List<RedemptionEntry>();
        var second = new List<RedemptionEntry>();
        var remainingFirstCapacity = firstDurationMinutes;

        foreach (var entry in entries)
        {
            var toFirst = Math.Min(remainingFirstCapacity, entry.MinutesCovered);
            if (toFirst > 0)
            {
                first.Add(new RedemptionEntry(entry.MissedBlockId, toFirst));
                remainingFirstCapacity -= toFirst;
            }

            var toSecond = entry.MinutesCovered - toFirst;
            if (toSecond > 0)
                second.Add(new RedemptionEntry(entry.MissedBlockId, toSecond));
        }

        return (first, second);
    }
}
