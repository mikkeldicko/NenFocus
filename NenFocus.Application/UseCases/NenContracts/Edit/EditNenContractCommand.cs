using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.NenContracts.Edit;

/// <summary>
/// Replaces a Scheduled contract in place: atomically deletes it and every block linked to
/// it, then reseals a new contract under the SAME ContractId from the wizard's edited
/// values — literal identity preservation, not just a UX illusion. One command, one
/// transaction, so nothing is ever left half-replaced (mirrors Seal's own atomicity and
/// BreakNenContractCommandHandler's block-cleanup atomicity). Field shape mirrors
/// SealNenContractCommand exactly — this is the same construction, just preceded by a
/// delete of what it's replacing.
/// </summary>
public sealed record EditNenContractCommand : ICommand<EditNenContractResult>
{
    /// <summary>The Scheduled contract being replaced.</summary>
    public Guid ContractId { get; init; }

    public string Name { get; init; } = string.Empty;
    public string Goal { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public StrictnessRule StrictnessRule { get; init; }
    public bool AllowCancellation { get; init; }
    public double? FixedDailyFloorHours { get; init; }
    public IReadOnlyList<DateOnly> RestDays { get; init; } = [];
    public TimeOnly? WorkHoursStart { get; init; }
    public TimeOnly? WorkHoursEnd { get; init; }
    public IReadOnlyList<DateOnly> NoWorkHoursDays { get; init; } = [];
    public IReadOnlyList<NenContractBlockSpec> Blocks { get; init; } = [];
    public Guid? ContractPresetId { get; init; }
    public RedemptionPlacementPolicy RedemptionPlacementPolicy { get; init; }
}
