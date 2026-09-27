using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

/// <summary>
/// Everything the Contract Wizard needs to re-open pre-filled for an existing Scheduled
/// contract: rules (mirroring SealNenContractCommand's own primitive fields, so the wizard
/// can hand this straight back through the normal Seal-shaped flow) plus the already-drafted
/// blocks (so drafting mode isn't a blank calendar).
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

    /// <summary>True if this contract was sealed via the Advanced path (DailyFloorPolicy.Default is set).</summary>
    public bool IsAdvanced { get; init; }

    /// <summary>Advanced-only. Null for a Simple contract (its floor is derived per day from Blocks).</summary>
    public double? FixedDailyFloorHours { get; init; }

    /// <summary>Advanced-only: days explicitly exempted from FixedDailyFloorHours.</summary>
    public IReadOnlyList<DateOnly> RestDays { get; init; } = [];

    public TimeOnly? WorkHoursStart { get; init; }
    public TimeOnly? WorkHoursEnd { get; init; }

    /// <summary>Advanced-only: days explicitly exempted from the work-hours window.</summary>
    public IReadOnlyList<DateOnly> NoWorkHoursDays { get; init; } = [];

    public Guid? ContractPresetId { get; init; }
    public RedemptionPlacementPolicy RedemptionPlacementPolicy { get; init; }
    public bool KeepBlockingDuringBreaks { get; init; }

    public IReadOnlyList<NenContractEditBlockDto> Blocks { get; init; } = [];
}
