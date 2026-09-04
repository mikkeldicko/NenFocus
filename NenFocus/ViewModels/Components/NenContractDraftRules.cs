using System;
using System.Collections.Generic;
using NenFocus.Domain.Enums;

namespace NenFocus.ViewModels.Components;

/// <summary>
/// Everything the contract rules wizard collected, assembled at its terminal step
/// (mirrors HatsuData's role in the Ritual). Consumed by PlannerPageViewModel to enter
/// drafting mode; combined with the drafted blocks into a SealNenContractCommand at Seal.
/// </summary>
public sealed record NenContractDraftRules
{
    public required string Name { get; init; }
    public required string Goal { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public required StrictnessRule StrictnessRule { get; init; }
    public required bool AllowCancellation { get; init; }
    public required RedemptionPlacementPolicy RedemptionPlacementPolicy { get; init; }

    /// <summary>Set for Advanced contracts; null for Simple (floor derived from drafted blocks).</summary>
    public double? FixedDailyFloorHours { get; init; }
    public IReadOnlyList<DateOnly> RestDays { get; init; } = [];
    public TimeOnly? WorkHoursStart { get; init; }
    public TimeOnly? WorkHoursEnd { get; init; }
    public Guid? ContractPresetId { get; init; }

    /// <summary>
    /// Non-null when this draft replaces an existing Scheduled contract rather than creating
    /// a new one — set by ContractWizardViewModel when it was opened in edit mode. Tells
    /// PlannerPageViewModel to seed drafting mode from the original blocks and to dispatch
    /// EditNenContractCommand (replace in place, same ContractId) instead of
    /// SealNenContractCommand at Seal.
    /// </summary>
    public Guid? EditingContractId { get; init; }
}
