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
        bool allowCancellation = false)
        => ContractPreset.Create(Guid.NewGuid(), name, strictnessRule, dailyFloor ?? new DailyFloor(4), workHours, allowCancellation);

    [Fact]
    public void Create_ValidInputs_Succeeds()
    {
        var preset = Create();

        preset.Name.Should().Be("Study Month");
        preset.StrictnessRule.Should().Be(StrictnessRule.Locked);
        preset.DailyFloor.Hours.Should().Be(4);
        preset.WorkHours.Should().BeNull();
        preset.AllowCancellation.Should().BeFalse();
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

        preset.Update("Renamed", StrictnessRule.PeriodTimeBudget, new DailyFloor(6), newWorkHours, allowCancellation: true);

        preset.Name.Should().Be("Renamed");
        preset.StrictnessRule.Should().Be(StrictnessRule.PeriodTimeBudget);
        preset.DailyFloor.Hours.Should().Be(6);
        preset.WorkHours.Should().Be(newWorkHours);
        preset.AllowCancellation.Should().BeTrue();
    }

    [Fact]
    public void Update_BlankName_Throws()
    {
        var preset = Create();

        var act = () => preset.Update("  ", StrictnessRule.Locked, new DailyFloor(4), null, false);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_TrimsName()
    {
        var preset = Create();

        preset.Update("  Renamed  ", StrictnessRule.Locked, new DailyFloor(4), null, false);

        preset.Name.Should().Be("Renamed");
    }
}
