using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

/// <summary>
/// A saved, reusable work/break cadence. Classic, Deep Work, and Flow ship as
/// ordinary seeded rows, not a special hardcoded case — a future custom preset
/// is just another row in this same table, freely creatable/editable/deletable
/// like any of them; there is no protected or built-in status.
///
/// No locking of any kind, unlike BlockPreset/ContractPreset: a Session
/// snapshots BreakTimer by value at Start (see BreakTimer), so nothing ever
/// holds a live reference to a BreakTimerPreset that editing or deleting one
/// could invalidate.
///
/// Authored on the Settings page, not the Presets page — deliberately too small
/// an adjustment to warrant its own tab there.
/// </summary>
public sealed class BreakTimerPreset : AggregateRoot
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public BreakTimer BreakTimer { get; private set; } = null!;

    private BreakTimerPreset() { } // EF Core

    public static BreakTimerPreset Create(Guid id, string name, BreakTimer breakTimer)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Preset name cannot be empty.", nameof(name));

        return new BreakTimerPreset
        {
            Id = id,
            Name = name.Trim(),
            BreakTimer = breakTimer
        };
    }

    /// <summary>
    /// Wholesale replace, mirroring ContractPreset.Update — every field is
    /// supplied together, and nothing here is a growing collection needing
    /// incremental Add/Remove invariants.
    /// </summary>
    public void Update(string name, BreakTimer breakTimer)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Preset name cannot be empty.", nameof(name));

        Name = name.Trim();
        BreakTimer = breakTimer;
    }
}
