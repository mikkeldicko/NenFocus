using NenFocus.Domain.Enums;

namespace NenFocus.Domain.Entities;

public sealed class PassiveBlock : PlannedBlock
{
    private PassiveBlock() { }

    public static PassiveBlock Create(
        Guid blockProfileId,
        DateTime start,
        DateTime end)
    {
        if (end <= start)
            throw new ArgumentException("End time must be after start time.");

        return new PassiveBlock
        {
            BlockProfileId = blockProfileId,
            Type = BlockType.Passive,
            Start = start,
            End = end,
            IsPlanned = true
        };
    }
}
