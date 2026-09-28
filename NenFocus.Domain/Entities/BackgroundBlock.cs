using NenFocus.Domain.Enums;

namespace NenFocus.Domain.Entities;

public sealed class BackgroundBlock : PlannedBlock
{
    private BackgroundBlock() { }

    public override BlockPresetType PresetType => BlockPresetType.Background;

    public static BackgroundBlock Create(
        Guid id,
        Guid blockPresetId,
        DateTime start,
        DateTime end,
        string? title = null,
        Guid? contractId = null)
    {
        if (end <= start)
            throw new ArgumentException("End time must be after start time.");

        return new BackgroundBlock
        {
            Id = id,
            BlockPresetId = blockPresetId,
            Start = start,
            End = end,
            Title = title,
            ContractId = contractId
        };
    }
}