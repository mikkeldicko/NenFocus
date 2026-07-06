using NenFocus.Domain.Enums;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed record CreatePlannedBlockCommand : ICommand<CreatePlannedBlockResult>
{
    public Guid PresetId { get; init; }
    public BlockPresetType BlockPresetType { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
}
