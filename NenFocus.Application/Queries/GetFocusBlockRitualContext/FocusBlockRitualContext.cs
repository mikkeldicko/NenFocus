using NenFocus.Application.DTOs;

namespace NenFocus.Application.Queries.GetFocusBlockRitualContext;

/// <summary>
/// LateBy/IsLate are computed here (query time, against ISystemClock) rather than left
/// for a ViewModel to derive from Schedule.Start — MODULE_STANDARDS.md's ViewModel
/// Forbidden Zone bans time-delta arithmetic in ViewModels, and this is the query
/// already fetching the block, so there's no reason to fetch it a second time just to
/// compute lateness elsewhere.
/// </summary>
public sealed record FocusBlockRitualContext
{
    public required BlockPresetDto Preset { get; init; }
    public required FocusBlockSchedule Schedule { get; init; }
    public required TimeSpan LateBy { get; init; }
    public required bool IsLate { get; init; }

    /// <summary>
    /// True when a Session already exists for this block (e.g. the sweep already marked it
    /// Missed under a different "now" than whatever triggered this ritual request — see
    /// DESIGN_GAP_HISTORY.md Gap 21's addenda). The caller must not open the Ritual in this
    /// case: a session can never be started for a block that already has one, so showing the
    /// Ritual here would only guarantee a rejection at Hatsu.
    /// </summary>
    public required bool AlreadyHasSession { get; init; }
}
