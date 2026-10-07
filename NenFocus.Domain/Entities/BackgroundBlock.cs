using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

public sealed class BackgroundBlock : PlannedBlock
{
    private BackgroundBlock() { }

    public override BlockPresetType PresetType => BlockPresetType.Background;

    public static BackgroundBlock Create(
        Guid id,
        Guid blockPresetId,
        TimeSlot scheduled,
        string? title = null,
        ContractMembership? membership = null)
    {
        // Only Focus time redeems (Gap 106): a Background block may be Committed or Bonus, never
        // Redemption — it would count toward the floor and be bound as one.
        if (membership is { IsRedemption: true })
            throw new ArgumentException("Only a Focus block can be a redemption block.", nameof(membership));

        return new BackgroundBlock
        {
            Id = id,
            BlockPresetId = blockPresetId,
            Scheduled = scheduled,
            Title = title,
            Membership = membership
        };
    }
}