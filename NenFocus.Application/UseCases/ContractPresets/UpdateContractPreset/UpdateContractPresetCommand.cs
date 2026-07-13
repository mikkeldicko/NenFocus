using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.ContractPresets.UpdateContractPreset;

public sealed record UpdateContractPresetCommand : ICommand<UpdateContractPresetResult>
{
    public Guid PresetId { get; init; }
    public string Name { get; init; } = string.Empty;
    public StrictnessRule StrictnessRule { get; init; }
    public double DailyFloorHours { get; init; }
    public TimeOnly? WorkHoursStart { get; init; }
    public TimeOnly? WorkHoursEnd { get; init; }
    public bool AllowCancellation { get; init; }
}
