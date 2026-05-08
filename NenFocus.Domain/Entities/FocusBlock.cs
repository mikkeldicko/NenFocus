using NenFocus.Domain.Enums;

namespace NenFocus.Domain.Entities;

public sealed class FocusBlock : PlannedBlock
{
    // SessionId retained for FocusBlock to link to sessions
    public Guid SessionId { get; private set; }

    private FocusBlock() { }

    public static FocusBlock Create(
        Guid sessionId,
        Guid blockProfileId,
        BlockType type,
        DateTime start,
        DateTime end,
        bool isPlanned = false)
    {
        if (end <= start)
            throw new ArgumentException("End time must be after start time.");

        return new FocusBlock
        {
            SessionId = sessionId,
            BlockProfileId = blockProfileId,
            Type = type,
            Start = start,
            End = end,
            IsPlanned = isPlanned
        };
    }
}
