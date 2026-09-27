using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetAvailableBreakTimerPresets;

/// <summary>
/// Presets Hatsu may actually offer for a given total session duration —
/// user-picked for a Spontaneous session, block-derived for a Planned one. A
/// preset is excluded when BreakTimer.ScheduleFor for this duration never
/// produces a single real break — the same
/// condition whether the very first Work period doesn't even fit, or a break
/// would leave nothing to work on afterward. One rule, read here and by the
/// live session engine alike.
/// </summary>
public record GetAvailableBreakTimerPresetsQuery(TimeSpan TotalDuration)
    : IQuery<IReadOnlyList<BreakTimerPresetDto>>;
