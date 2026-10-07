using System;
using System.Collections.Generic;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

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
    public required bool KeepBlockingDuringBreaks { get; init; }
    public required bool BonusBlocksFollowStrictness { get; init; }

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

    // Bridge (Gap 92, option B): the Seal/Edit commands carry typed settings, the wizard chain still
    // carries loose ones. Both come from a saved preset, which is valid by construction (whole-hour
    // floor, end after start), so neither conversion can lose anything. Gaps 68 and 82 carry typed
    // values end to end and delete these.

    /// <summary>The Work Hours as the Seal/Edit commands take them.</summary>
    public WorkHours? WorkHours => WorkHoursStart is { } start && WorkHoursEnd is { } end ? new WorkHours(start, end) : null;

    /// <summary>The fixed floor as the Seal/Edit commands take it: whole hours.</summary>
    public int? WholeFixedDailyFloorHours => FixedDailyFloorHours is { } hours ? (int)hours : null;
}
