using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

public sealed record ContractPresetDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public StrictnessRule StrictnessRule { get; init; }
    public double DailyFloorHours { get; init; }
    public TimeOnly? WorkHoursStart { get; init; }
    public TimeOnly? WorkHoursEnd { get; init; }
    public bool AllowCancellation { get; init; }

    public static ContractPresetDto FromEntity(ContractPreset preset) => new()
    {
        Id = preset.Id,
        Name = preset.Name,
        StrictnessRule = preset.StrictnessRule,
        DailyFloorHours = preset.DailyFloor.Hours,
        WorkHoursStart = preset.WorkHours?.Start,
        WorkHoursEnd = preset.WorkHours?.End,
        AllowCancellation = preset.AllowCancellation
    };
}
