using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.NenContracts.Seal;

/// <summary>
/// Atomically creates a Nen Contract together with every block scheduled for it in the
/// wizard — nothing is persisted until this command commits (see DESIGN_GAP_HISTORY.md
/// / the Seal atomicity decision). Simple vs Advanced is a wizard-only distinction: by
/// the time this command is dispatched, the wizard has already resolved which mode was
/// used into these same primitive fields.
/// </summary>
public sealed record SealNenContractCommand : ICommand<SealNenContractResult>
{
    public string Name { get; init; } = string.Empty;
    public string Goal { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public StrictnessRule StrictnessRule { get; init; }
    public bool AllowCancellation { get; init; }

    /// <summary>
    /// Set for Advanced contracts: one fixed floor applied to every day except <see cref="RestDays"/>.
    /// Null for Simple contracts: each day's floor is instead derived from that day's own
    /// planned Focus hours (computed from <see cref="Blocks"/>), so every planned day passes
    /// by construction and every unplanned day is a rest day automatically.
    /// </summary>
    public double? FixedDailyFloorHours { get; init; }

    /// <summary>Advanced-only: days that are exempt from <see cref="FixedDailyFloorHours"/>.</summary>
    public IReadOnlyList<DateOnly> RestDays { get; init; } = [];

    /// <summary>
    /// Advanced-only, optional: one work-hours window applied to every day except
    /// <see cref="NoWorkHoursDays"/>. Both must be set together, or both left null — there
    /// is no work hours constraint at all if either is missing. Simple contracts always
    /// leave both null (Work Hours is Advanced-only, per DOMAIN_DICTIONARY.md).
    /// </summary>
    public TimeOnly? WorkHoursStart { get; init; }
    public TimeOnly? WorkHoursEnd { get; init; }

    /// <summary>
    /// Advanced-only: days that are exempt from the work-hours window — same per-day
    /// exception shape as <see cref="RestDays"/> is for the daily floor, not a different
    /// window on those days, mirroring how Advanced contracts don't expose a different
    /// non-null floor per day either, only the floor or a rest-day exemption from it.
    /// </summary>
    public IReadOnlyList<DateOnly> NoWorkHoursDays { get; init; } = [];

    public IReadOnlyList<NenContractBlockSpec> Blocks { get; init; } = [];

    /// <summary>
    /// Set when this contract was sealed via the wizard's "Start from preset" flow.
    /// The preset's values were already copied into this command's own fields at that
    /// point (copy, not a live link) — this is stored purely so ContractPreset deletion
    /// can check whether a live commitment still traces back to it.
    /// </summary>
    public Guid? ContractPresetId { get; init; }

    /// <summary>
    /// Which days a redemption block may originate a new deficit claim on — copied from
    /// the picked ContractPreset (Advanced) or hardcoded AnywhereInContract (Simple), the
    /// same split every other rule field already follows.
    /// </summary>
    public RedemptionPlacementPolicy RedemptionPlacementPolicy { get; init; }
}
