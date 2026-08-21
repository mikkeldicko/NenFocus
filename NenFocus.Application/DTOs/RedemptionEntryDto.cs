namespace NenFocus.Application.DTOs;

/// <summary>
/// One missed/late block a redemption FocusBlock covers, and how much of its deficit.
/// Raw input — validated when converted to the Domain RedemptionEntry value object.
/// </summary>
public sealed record RedemptionEntryDto(Guid MissedBlockId, double MinutesCovered);
