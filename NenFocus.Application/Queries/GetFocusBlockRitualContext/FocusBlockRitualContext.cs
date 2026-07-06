using NenFocus.Application.DTOs;

namespace NenFocus.Application.Queries.GetFocusBlockRitualContext;

public sealed record FocusBlockRitualContext
{
    public required BlockPresetDto Preset { get; init; }
    public required FocusBlockSchedule Schedule { get; init; }
}
