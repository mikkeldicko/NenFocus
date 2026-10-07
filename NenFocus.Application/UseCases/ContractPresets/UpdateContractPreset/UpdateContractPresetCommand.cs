using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.ContractPresets.UpdateContractPreset;

public sealed record UpdateContractPresetCommand : ICommand<UpdateContractPresetResult>
{
    public Guid PresetId { get; init; }
    public string Name { get; init; } = string.Empty;
    public StrictnessRule StrictnessRule { get; init; }
    /// <summary>Whole hours, or null for no fixed floor; 1–24 and fitting the Work Hours are ContractPreset.CheckSettings' (Gap 92, V9).</summary>
    public int? DailyFloorHours { get; init; }
    public WorkHours? WorkHours { get; init; }
    public bool AllowCancellation { get; init; }
    public RedemptionPlacementPolicy RedemptionPlacementPolicy { get; init; }
    public bool KeepBlockingDuringBreaks { get; init; }
    public bool BonusBlocksFollowStrictness { get; init; }
}
