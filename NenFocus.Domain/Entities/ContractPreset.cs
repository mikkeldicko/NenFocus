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
        EnsureFits(rules, workHours);

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
        EnsureFits(rules, workHours);

        Name = name.Trim();
        Rules = rules with { };
        WorkHours = workHours;
    }

    /// <summary>
    /// Can this fixed floor and these Work Hours go together? A floor of whole hours from 1 to 24
    /// (<see cref="ContractRules.IsValidFixedFloor"/>) that fits the window (<see cref="DailyFloor.FitsIn"/>).
    /// The one question the preset form and the Seal/Edit validators ask — a contract's settings are a
    /// preset's copy — and the one Create and Update obey, so a preset can't be impossible (Gap 92, V9).
    /// </summary>
    public static ContractSettingsOutcome CheckSettings(int? fixedFloorHours, WorkHours? workHours)
    {
        if (fixedFloorHours is not { } hours) return ContractSettingsOutcome.Allowed;
        if (!ContractRules.IsValidFixedFloor(hours)) return ContractSettingsOutcome.FloorOutOfRange;
        if (workHours is not null && !new DailyFloor(hours).FitsIn(workHours.Length)) return ContractSettingsOutcome.FloorLongerThanWorkHours;
        return ContractSettingsOutcome.Allowed;
    }

    private static void EnsureFits(ContractRules rules, WorkHours? workHours)
    {
        // A fixed floor is whole hours by construction (ContractRules), so the cast is exact.
        var outcome = CheckSettings((int?)rules.FixedDailyFloor?.Hours, workHours);
        if (outcome != ContractSettingsOutcome.Allowed)
            throw new ArgumentException($"These settings can't go together ({outcome}).", nameof(workHours));
    }
}
