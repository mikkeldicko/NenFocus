using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

public sealed record ContractBlockBatchPreparation
{
    public ContractBlockBatchOutcome Outcome { get; init; }
    public bool Success => Outcome == ContractBlockBatchOutcome.Valid;
    public IReadOnlyList<Block> Blocks { get; init; } = [];
    public IReadOnlyDictionary<DateOnly, double> PlannedFocusHoursByDay { get; init; } = new Dictionary<DateOnly, double>();

    public static ContractBlockBatchPreparation Fail(ContractBlockBatchOutcome outcome) => new() { Outcome = outcome };

    public static ContractBlockBatchPreparation Ok(
        IReadOnlyList<Block> blocks,
        IReadOnlyDictionary<DateOnly, double> plannedFocusHoursByDay) => new()
    {
        Outcome = ContractBlockBatchOutcome.Valid,
        Blocks = blocks,
        PlannedFocusHoursByDay = plannedFocusHoursByDay
    };
}
