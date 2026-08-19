using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.ContractPresets.CreateContractPreset;

public sealed record CreateContractPresetCommand : ICommand<CreateContractPresetResult>
{
    public string Name { get; init; } = string.Empty;
    public StrictnessRule StrictnessRule { get; init; }
    public double? DailyFloorHours { get; init; }
    public TimeOnly? WorkHoursStart { get; init; }
    public TimeOnly? WorkHoursEnd { get; init; }
    public bool AllowCancellation { get; init; }
}
