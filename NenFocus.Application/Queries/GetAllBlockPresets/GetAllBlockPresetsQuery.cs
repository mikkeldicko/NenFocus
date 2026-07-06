using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Queries.GetAllBlockPresets;

public sealed record GetAllBlockPresetsQuery : IQuery<IReadOnlyList<BlockPresetDto>>
{
    public string? Tag { get; init; }
    public BlockPresetType? PresetType { get; init; }
}
