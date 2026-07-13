namespace NenFocus.Application.DTOs;

/// <summary>
/// One missed/late block a make-up FocusBlock covers, and how much of its deficit.
/// Raw input — validated when converted to the Domain MakeUpForEntry value object.
/// </summary>
public sealed record MakeUpForEntryDto(Guid MissedBlockId, double MinutesCovered);
