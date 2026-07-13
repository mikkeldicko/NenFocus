using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Domain.Services;

public sealed record ContractBlockBatchPreparation
{
    public ContractBlockBatchOutcome Outcome { get; init; }
    public bool Success => Outcome == ContractBlockBatchOutcome.Valid;
    public IReadOnlyList<PlannedBlock> Blocks { get; init; } = [];
    public IReadOnlyList<FocusBlock> FocusBlocks { get; init; } = [];
    public IReadOnlyDictionary<DateOnly, double> PlannedFocusHoursByDay { get; init; } = new Dictionary<DateOnly, double>();

    public static ContractBlockBatchPreparation Fail(ContractBlockBatchOutcome outcome) => new() { Outcome = outcome };

    public static ContractBlockBatchPreparation Ok(
        IReadOnlyList<PlannedBlock> blocks,
        IReadOnlyList<FocusBlock> focusBlocks,
        IReadOnlyDictionary<DateOnly, double> plannedFocusHoursByDay) => new()
    {
        Outcome = ContractBlockBatchOutcome.Valid,
        Blocks = blocks,
        FocusBlocks = focusBlocks,
        PlannedFocusHoursByDay = plannedFocusHoursByDay
    };
}
