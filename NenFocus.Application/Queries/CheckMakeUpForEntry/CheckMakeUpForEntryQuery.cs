using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.CheckMakeUpForEntry;

/// <summary>
/// Validates one proposed MakeUpForEntry against Make-Up Rules 4, 8, 9, 10 and the
/// referenced block's remaining deficit — shared by CreatePlannedBlockValidator and
/// UpdatePlannedBlockValidator so the same check isn't derived twice.
/// </summary>
/// <param name="PresetId">The make-up block's own preset — must match the missed block's.</param>
/// <param name="BlockDate">The make-up block's scheduled date, for the same-day check.</param>
/// <param name="MissedBlockId">The block this entry claims to cover.</param>
/// <param name="MinutesCovered">How much of that block's deficit this entry covers.</param>
/// <param name="ExcludeBlockId">
/// On update, the block being edited — its own prior contribution to MissedBlockId is
/// backed out of "remaining deficit" first, since the not-yet-persisted new value would
/// otherwise be compared against a deficit figure that still counts the old one as covered.
/// </param>
public sealed record CheckMakeUpForEntryQuery(
    Guid PresetId,
    DateTime BlockDate,
    Guid MissedBlockId,
    double MinutesCovered,
    Guid? ExcludeBlockId) : IQuery<MakeUpForEntryCheckResult>;
