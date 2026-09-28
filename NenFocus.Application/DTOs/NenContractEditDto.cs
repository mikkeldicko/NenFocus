using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

/// <summary>
/// Everything the Contract Wizard needs to re-open pre-filled for an existing Scheduled
/// contract: its rules, its days (the only truth for each day's window and floor — Gap 65),
/// plus the already-drafted blocks (so drafting mode isn't a blank calendar).
/// </summary>
public sealed record NenContractEditDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Goal { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public StrictnessRule StrictnessRule { get; init; }
    public bool AllowCancellation { get; init; }

    /// <summary>The contract's DailyFloorRule when it is Fixed — null when floors follow the plan or there are none.</summary>
    public double? FixedDailyFloorHours { get; init; }

    /// <summary>One entry per contract day, first to last.</summary>
    public IReadOnlyList<NenContractEditDayDto> Days { get; init; } = [];

    public Guid? ContractPresetId { get; init; }
    public RedemptionPlacementPolicy RedemptionPlacementPolicy { get; init; }
    public bool KeepBlockingDuringBreaks { get; init; }

    public IReadOnlyList<NenContractEditBlockDto> Blocks { get; init; } = [];
}
