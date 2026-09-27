using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

/// <summary>
/// A saved, reusable template of Nen Contract configuration rules — StrictnessRule,
/// DailyFloor, WorkHours, AllowCancellation. Deliberately excludes rest days, work-hours
/// per-day exceptions, and the date range: all three are date-bound and can't generalize
/// across the arbitrary future date range a preset gets applied to. Copied by value into
/// the wizard's draft fields when selected ("Start from preset") — the resulting
/// NenContract stores its own independent copy of every value, not a live reference.
/// </summary>
public sealed class ContractPreset : AggregateRoot
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public StrictnessRule StrictnessRule { get; private set; }
    public DailyFloor? DailyFloor { get; private set; }
    public WorkHours? WorkHours { get; private set; }
    public bool AllowCancellation { get; private set; }

    /// <summary>
    /// Which days a redemption block placed under a contract sealed from this preset may
    /// originate a new deficit claim on — see RedemptionPlacementPolicy and
    /// NenContract.CheckRedemptionEligibility.
    /// </summary>
    public RedemptionPlacementPolicy RedemptionPlacementPolicy { get; private set; }

    /// <summary>
    /// Whether a Focus Block committed to a contract sealed from this preset should
    /// stop enforcing while its Session is inside a break slot. Can only ever narrow the global
    /// Settings default toward stricter (keep blocking), never loosen it — see
    /// IPolicyEngine.ComputeSlice and NenContract.PauseEnforcementDuringBreak. Not
    /// yet user-editable: Settings, where this would actually be set, doesn't exist
    /// yet, so this defaults permissive (true) until it does.
    /// </summary>
    public bool PauseEnforcementDuringBreak { get; private set; }

    private ContractPreset() { } // EF Core

    public static ContractPreset Create(
        Guid id,
        string name,
        StrictnessRule strictnessRule,
        DailyFloor? dailyFloor,
        WorkHours? workHours,
        bool allowCancellation,
        RedemptionPlacementPolicy redemptionPlacementPolicy = RedemptionPlacementPolicy.AnywhereInContract,
        bool pauseEnforcementDuringBreak = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Preset name cannot be empty.", nameof(name));

        return new ContractPreset
        {
            Id = id,
            Name = name.Trim(),
            StrictnessRule = strictnessRule,
            DailyFloor = dailyFloor,
            WorkHours = workHours,
            AllowCancellation = allowCancellation,
            RedemptionPlacementPolicy = redemptionPlacementPolicy,
            PauseEnforcementDuringBreak = pauseEnforcementDuringBreak
        };
    }

    /// <summary>
    /// Wholesale replace — every field is supplied together, matching how the Presets
    /// page edit form actually submits. No granular per-field mutators: unlike
    /// BlockPreset, nothing here is a growing collection needing incremental Add/Remove
    /// invariants.
    /// </summary>
    public void Update(
        string name,
        StrictnessRule strictnessRule,
        DailyFloor? dailyFloor,
        WorkHours? workHours,
        bool allowCancellation,
        RedemptionPlacementPolicy redemptionPlacementPolicy = RedemptionPlacementPolicy.AnywhereInContract,
        bool pauseEnforcementDuringBreak = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Preset name cannot be empty.", nameof(name));

        Name = name.Trim();
        StrictnessRule = strictnessRule;
        DailyFloor = dailyFloor;
        WorkHours = workHours;
        AllowCancellation = allowCancellation;
        RedemptionPlacementPolicy = redemptionPlacementPolicy;
        PauseEnforcementDuringBreak = pauseEnforcementDuringBreak;
    }
}
