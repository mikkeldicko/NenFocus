using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain;

public class ContractPresetTests
{
    private static ContractPreset Create(
        string name = "Study Month",
        StrictnessRule strictnessRule = StrictnessRule.Locked,
        DailyFloor? dailyFloor = null,
        WorkHours? workHours = null,
        bool allowCancellation = false,
        bool omitDailyFloor = false)
        => ContractPreset.Create(Guid.NewGuid(), name,
            TestRules.Of(strictnessRule, omitDailyFloor ? null : dailyFloor ?? new DailyFloor(4), allowCancellation), workHours);

    [Fact]
    public void Create_ValidInputs_Succeeds()
    {
        var preset = Create();

        preset.Name.Should().Be("Study Month");
        preset.StrictnessRule.Should().Be(StrictnessRule.Locked);
        preset.DailyFloor!.Hours.Should().Be(4);
        preset.WorkHours.Should().BeNull();
        preset.AllowCancellation.Should().BeFalse();
    }

    [Fact]
    public void Create_NoDailyFloor_StoresNull()
    {
        var preset = Create(omitDailyFloor: true);

        preset.DailyFloor.Should().BeNull();
    }

    [Fact]
    public void Create_BlankName_Throws()
    {
        var act = () => Create(name: "   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_TrimsName()
    {
        var preset = Create(name: "  Study Month  ");

        preset.Name.Should().Be("Study Month");
    }

    [Fact]
    public void Create_WithWorkHours_StoresIt()
    {
        var workHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0));

        var preset = Create(workHours: workHours);

        preset.WorkHours.Should().Be(workHours);
    }

    [Fact]
    public void Update_ReplacesEveryField()
    {
        var preset = Create();
        var newWorkHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0));

        preset.Update("Renamed",
            TestRules.Of(StrictnessRule.PeriodTimeBudget, new DailyFloor(6), allowCancellation: true, keepBlockingDuringBreaks: true,
                RedemptionPlacementPolicy.CurrentDayOnly, bonusBlocksFollowStrictness: true),
            newWorkHours);

        preset.Name.Should().Be("Renamed");
        preset.StrictnessRule.Should().Be(StrictnessRule.PeriodTimeBudget);
        preset.DailyFloor!.Hours.Should().Be(6);
        preset.WorkHours.Should().Be(newWorkHours);
        preset.AllowCancellation.Should().BeTrue();
        preset.KeepBlockingDuringBreaks.Should().BeTrue();
        preset.RedemptionPlacementPolicy.Should().Be(RedemptionPlacementPolicy.CurrentDayOnly);
        preset.Rules.BonusBlocksFollowStrictness.Should().BeTrue();
    }

    [Fact]
    public void Update_ToNoDailyFloor_StoresNull()
    {
        var preset = Create();

        preset.Update("Renamed", TestRules.Of(StrictnessRule.PeriodTimeBudget), null);

        preset.DailyFloor.Should().BeNull();
    }

    [Fact]
    public void Update_BlankName_Throws()
    {
        var preset = Create();

        var act = () => preset.Update("  ", TestRules.Of(fixedDailyFloor: new DailyFloor(4)), null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_TrimsName()
    {
        var preset = Create();

        preset.Update("  Renamed  ", TestRules.Of(fixedDailyFloor: new DailyFloor(4)), null);

        preset.Name.Should().Be("Renamed");
    }

    [Fact]
    public void Create_ByDefault_DoesNotKeepBlockingDuringBreaks()
    {
        Create().KeepBlockingDuringBreaks.Should().BeFalse();
    }
}
