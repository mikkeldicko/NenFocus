using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetAllBreakTimerPresets;

public sealed record GetAllBreakTimerPresetsQuery : IQuery<IReadOnlyList<BreakTimerPresetDto>>;
