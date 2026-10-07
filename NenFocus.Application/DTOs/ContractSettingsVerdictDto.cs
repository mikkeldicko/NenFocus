using NenFocus.Domain.Enums;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.DTOs;

/// <summary>
/// Whether a fixed daily floor and Work Hours can go together (<c>ContractPreset.CheckSettings</c>,
/// Gap 92), with the one user-facing sentence. The preset and Seal/Edit validators all go through it.
/// Gap 100 moves the sentences out to the front end.
/// </summary>
public sealed record ContractSettingsVerdictDto
{
    public ContractSettingsOutcome Outcome { get; init; }
    public string? Message { get; init; }

    public static ContractSettingsVerdictDto Check(int? fixedFloorHours, WorkHours? workHours)
    {
        var outcome = ContractPreset.CheckSettings(fixedFloorHours, workHours);
        return new()
        {
            Outcome = outcome,
            Message = outcome switch
            {
                ContractSettingsOutcome.FloorOutOfRange => "Daily floor must be a whole number of hours from 1 to 24.",
                ContractSettingsOutcome.FloorLongerThanWorkHours => "Daily floor cannot exceed the total work hours window.",
                _ => null
            }
        };
    }
}
