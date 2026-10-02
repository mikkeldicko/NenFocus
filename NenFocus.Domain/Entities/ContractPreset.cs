using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

/// <summary>
/// A saved, reusable template of Nen Contract configuration — its ContractRules, plus default
/// WorkHours for drafting. Deliberately excludes rest days, work-hours per-day exceptions, and
/// the date range: all three are date-bound and can't generalize across the arbitrary future
/// date range a preset gets applied to. Copied by value into the wizard's draft fields when
/// selected ("Start from preset") — the resulting NenContract stores its own independent copy
/// of every value, not a live reference.
/// </summary>
public sealed class ContractPreset : AggregateRoot
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;

    /// <summary>The rules a contract sealed from this preset gets — the same value object a NenContract holds (Gap 93).</summary>
    public ContractRules Rules { get; private set; } = null!;

    public WorkHours? WorkHours { get; private set; }

    public StrictnessRule StrictnessRule => Rules.Strictness;
    public DailyFloor? DailyFloor => Rules.FixedDailyFloor;
    public bool AllowCancellation => Rules.AllowCancellation;
    public RedemptionPlacementPolicy RedemptionPlacementPolicy => Rules.RedemptionPlacement;
    public bool KeepBlockingDuringBreaks => Rules.KeepBlockingDuringBreaks;

    private ContractPreset() { } // EF Core

    public static ContractPreset Create(Guid id, string name, ContractRules rules, WorkHours? workHours)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Preset name cannot be empty.", nameof(name));

        return new ContractPreset
        {
            Id = id,
            Name = name.Trim(),
            // Its own copy: a stored value object belongs to one row (Gap 97's lesson).
            Rules = rules with { },
            WorkHours = workHours
        };
    }

    /// <summary>
    /// Wholesale replace — every field is supplied together, matching how the Presets
    /// page edit form actually submits. No granular per-field mutators: unlike
    /// BlockPreset, nothing here is a growing collection needing incremental Add/Remove
    /// invariants. The rules arrive as one value, so a caller can't forget one (Gap 64's trap).
    /// </summary>
    public void Update(string name, ContractRules rules, WorkHours? workHours)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Preset name cannot be empty.", nameof(name));

        Name = name.Trim();
        Rules = rules with { };
        WorkHours = workHours;
    }
}
