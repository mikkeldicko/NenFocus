using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests;

/// <summary>A ContractRules with every setting at its plain default unless a test says otherwise (Gap 93).</summary>
internal static class TestRules
{
    public static ContractRules Of(
        StrictnessRule strictness = StrictnessRule.Locked,
        DailyFloor? fixedDailyFloor = null,
        bool allowCancellation = false,
        bool keepBlockingDuringBreaks = false,
        RedemptionPlacementPolicy redemptionPlacement = RedemptionPlacementPolicy.AnywhereInContract,
        bool bonusBlocksFollowStrictness = false)
        => new(strictness, fixedDailyFloor, allowCancellation, keepBlockingDuringBreaks, redemptionPlacement, bonusBlocksFollowStrictness);
}
