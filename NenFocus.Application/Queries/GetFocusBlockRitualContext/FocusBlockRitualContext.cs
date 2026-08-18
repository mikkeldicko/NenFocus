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
}
