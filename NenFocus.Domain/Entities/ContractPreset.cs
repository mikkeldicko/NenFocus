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
    /// Whether sessions under a contract sealed from this preset keep their block
    /// preset enforced through their breaks, instead of it lifting (Gap 64). Only
    /// ever makes breaks stricter; false is today's behavior. Copied to the contract
    /// at Seal, and from there onto each of its sessions at Start.
    /// </summary>
    public bool KeepBlockingDuringBreaks { get; private set; }

    private ContractPreset() { } // EF Core

    public static ContractPreset Create(
        Guid id,
        string name,
        StrictnessRule strictnessRule,
        DailyFloor? dailyFloor,
        WorkHours? workHours,
        bool allowCancellation,
        RedemptionPlacementPolicy redemptionPlacementPolicy = RedemptionPlacementPolicy.AnywhereInContract,
        bool keepBlockingDuringBreaks = false)
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
            KeepBlockingDuringBreaks = keepBlockingDuringBreaks
        };
    }

    /// <summary>
    /// Wholesale replace — every field is supplied together, matching how the Presets
    /// page edit form actually submits. No granular per-field mutators: unlike
    /// BlockPreset, nothing here is a growing collection needing incremental Add/Remove
    /// invariants. Every rule is a required parameter on purpose: a default here would
    /// silently reset any rule a caller forgot to pass (Gap 64's trap).
    /// </summary>
    public void Update(
        string name,
        StrictnessRule strictnessRule,
        DailyFloor? dailyFloor,
        WorkHours? workHours,
        bool allowCancellation,
        RedemptionPlacementPolicy redemptionPlacementPolicy,
        bool keepBlockingDuringBreaks)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Preset name cannot be empty.", nameof(name));

        Name = name.Trim();
        StrictnessRule = strictnessRule;
        DailyFloor = dailyFloor;
        WorkHours = workHours;
        AllowCancellation = allowCancellation;
        RedemptionPlacementPolicy = redemptionPlacementPolicy;
        KeepBlockingDuringBreaks = keepBlockingDuringBreaks;
    }
}
