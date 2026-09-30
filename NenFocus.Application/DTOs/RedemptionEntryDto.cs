using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.DTOs;

/// <summary>
/// One missed/late block a redemption FocusBlock covers, and how much of its deficit.
/// Raw input — validated when converted to the Domain RedemptionEntry value object.
/// </summary>
public sealed record RedemptionEntryDto(Guid MissedBlockId, double MinutesCovered)
{
    /// <summary>The one mapping from a stored entry (one builder per DTO).</summary>
    public static RedemptionEntryDto From(RedemptionEntry entry) => new(entry.MissedBlockId, entry.MinutesCovered);
}
