using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.ViewModels.Contracts;

namespace NenFocus.Tests.ViewModels.Contracts;

public class ContractEditPrefillTests
{
    private static readonly DateOnly Mon = new(2026, 7, 6);

    private static NenContractEditDayDto Day(DateOnly date, int startHour, int? endHour, double? floor) => new(
        date,
        date.ToDateTime(new TimeOnly(startHour, 0)),
        endHour is { } h ? date.ToDateTime(new TimeOnly(h, 0)) : date.AddDays(1).ToDateTime(TimeOnly.MinValue),
        floor);

    private static NenContractEditDto Contract(double? fixedFloor, params NenContractEditDayDto[] days) =>
        new() { FixedDailyFloorHours = fixedFloor, Days = days };

    [Fact]
    public void From_FixedFloorWithWorkHours_RebuildsTheWizardFields()
    {
        // Day 1 started late (12:00); a rest day; a no-Work-Hours day.
        var prefill = ContractEditPrefill.From(Contract(4,
            Day(Mon, 12, 17, 4),
            Day(Mon.AddDays(1), 9, 17, null),
            Day(Mon.AddDays(2), 0, null, 4),
            Day(Mon.AddDays(3), 9, 17, 4)));

        prefill.IsAdvanced.Should().BeTrue();
        prefill.FixedDailyFloorHours.Should().Be(4);
        prefill.RestDays.Should().Equal(Mon.AddDays(1));
        prefill.WorkHoursStart.Should().Be(new TimeOnly(9, 0)); // not day 1's late start
        prefill.WorkHoursEnd.Should().Be(new TimeOnly(17, 0));
    }

    [Fact]
    public void From_FollowsPlanWithoutWorkHours_IsSimpleWithNoWorkHours()
    {
        var prefill = ContractEditPrefill.From(Contract(null,
            Day(Mon, 0, null, 2),
            Day(Mon.AddDays(1), 0, null, null)));

        prefill.IsAdvanced.Should().BeFalse();
        prefill.RestDays.Should().BeEmpty();
        prefill.WorkHoursStart.Should().BeNull();
        prefill.WorkHoursEnd.Should().BeNull();
    }

    [Fact]
    public void From_TwoDayContractWithLateStart_TakesWorkHoursFromTheLastDay()
    {
        var prefill = ContractEditPrefill.From(Contract(null,
            Day(Mon, 12, 17, null),
            Day(Mon.AddDays(1), 9, 17, null)));

        prefill.WorkHoursStart.Should().Be(new TimeOnly(9, 0));
        prefill.WorkHoursEnd.Should().Be(new TimeOnly(17, 0));
    }
}
