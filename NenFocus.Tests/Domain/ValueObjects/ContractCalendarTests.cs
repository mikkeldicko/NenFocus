using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

public class ContractCalendarTests
{
    private static readonly DateOnly Mon = new(2026, 7, 6);
    private static readonly DateOnly Tue = Mon.AddDays(1);
    private static readonly DateOnly Wed = Mon.AddDays(2);
    private static readonly WorkHours NineToFive = new(new TimeOnly(9, 0), new TimeOnly(17, 0));
    private static readonly IReadOnlyCollection<DateOnly> NoDates = [];
    private static readonly IReadOnlyDictionary<DateOnly, double> NoPlan = new Dictionary<DateOnly, double>();

    private static DateTime At(DateOnly date, int hour, int minute = 0) => date.ToDateTime(new TimeOnly(hour, minute));
    private static DateTime Midnight(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);
    private static ContractDay FullDay(DateOnly date, DailyFloor? floor = null) => new(date, Midnight(date), Midnight(date.AddDays(1)), floor);

    // ---- Constructor invariant: an unbroken run, one day per date ----

    [Fact]
    public void Constructor_ConsecutiveDays_DerivesItsBoundaries()
    {
        var calendar = new ContractCalendar([
            new ContractDay(Mon, At(Mon, 12), At(Mon, 17), null),
            FullDay(Tue),
            new ContractDay(Wed, At(Wed, 9), At(Wed, 15), null)
        ]);

        calendar.FirstDate.Should().Be(Mon);
        calendar.LastDate.Should().Be(Wed);
        calendar.StartsAt.Should().Be(At(Mon, 12));
        calendar.EndsAt.Should().Be(At(Wed, 15));
    }

    [Fact]
    public void Constructor_UnorderedDays_AreSortedByDate()
    {
        var calendar = new ContractCalendar([FullDay(Tue), FullDay(Mon)]);

        calendar.Days.Select(d => d.Date).Should().Equal(Mon, Tue);
    }

    [Fact]
    public void Constructor_NoDays_Throws()
    {
        var act = () => new ContractCalendar([]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_GapBetweenDays_Throws()
    {
        var act = () => new ContractCalendar([FullDay(Mon), FullDay(Wed)]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_DuplicateDate_Throws()
    {
        var act = () => new ContractCalendar([FullDay(Mon), FullDay(Mon)]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DayOn_ReturnsTheDay_OrNullOutsideTheCalendar()
    {
        var calendar = new ContractCalendar([FullDay(Mon), FullDay(Tue)]);

        calendar.DayOn(Tue)!.Date.Should().Be(Tue);
        calendar.DayOn(Wed).Should().BeNull();
    }

    [Fact]
    public void HasAnyDailyFloor_TrueOnlyWhenSomeDayHasAFloor()
    {
        new ContractCalendar([FullDay(Mon), FullDay(Tue)]).HasAnyDailyFloor.Should().BeFalse();
        new ContractCalendar([FullDay(Mon), FullDay(Tue, new DailyFloor(2))]).HasAnyDailyFloor.Should().BeTrue();
    }

    // ---- EndingOn (Break) ----

    [Fact]
    public void EndingOn_DropsLaterDays_AndKeepsTheLastDayWhole()
    {
        var calendar = new ContractCalendar([FullDay(Mon), FullDay(Tue, new DailyFloor(4)), FullDay(Wed)]);

        var shortened = calendar.EndingOn(Tue);

        shortened.LastDate.Should().Be(Tue);
        shortened.DayOn(Tue).Should().Be(FullDay(Tue, new DailyFloor(4)));
    }

    [Fact]
    public void EndingOn_OnOrAfterTheLastDate_ChangesNothing()
    {
        var calendar = new ContractCalendar([FullDay(Mon), FullDay(Tue)]);

        calendar.EndingOn(Wed).Days.Should().Equal(calendar.Days);
    }

    [Fact]
    public void EndingOn_BeforeTheFirstDate_KeepsTheFirstDay()
    {
        var calendar = new ContractCalendar([FullDay(Mon), FullDay(Tue)]);

        calendar.EndingOn(Mon.AddDays(-3)).Days.Select(d => d.Date).Should().Equal(Mon);
    }

    // ---- Generate: windows ----

    [Fact]
    public void Generate_WithoutWorkHours_FullDays_FirstDayFromStart_LastDayToEndTime()
    {
        var calendar = ContractCalendar.Generate(
            localStart: At(Mon, 9), endDate: Wed, endTime: new TimeOnly(17, 0),
            workHours: null, noWorkHoursDays: NoDates, floorRule: new DailyFloorRule.None(), restDays: NoDates, plannedFocusHoursByDay: NoPlan);

        calendar.Days.Should().Equal(
            new ContractDay(Mon, At(Mon, 9), Midnight(Tue), null),
            FullDay(Tue),
            new ContractDay(Wed, Midnight(Wed), At(Wed, 17), null));
    }

    [Fact]
    public void Generate_WithoutWorkHours_NoEndTime_RunsToTheEndOfTheLastDay()
    {
        var calendar = ContractCalendar.Generate(
            At(Mon, 9), Tue, endTime: null, workHours: null, NoDates, new DailyFloorRule.None(), NoDates, NoPlan);

        calendar.EndsAt.Should().Be(Midnight(Wed));
    }

    [Fact]
    public void Generate_WithWorkHours_EveryDayIsTheWorkHoursWindow_StartingAtTheChosenStart()
    {
        var calendar = ContractCalendar.Generate(
            At(Mon, 12), Wed, endTime: null, NineToFive, NoDates, new DailyFloorRule.None(), NoDates, NoPlan);

        calendar.Days.Should().Equal(
            new ContractDay(Mon, At(Mon, 12), At(Mon, 17), null),
            new ContractDay(Tue, At(Tue, 9), At(Tue, 17), null),
            new ContractDay(Wed, At(Wed, 9), At(Wed, 17), null));
        calendar.EndsAt.Should().Be(At(Wed, 17));
    }

    [Fact]
    public void Generate_WithWorkHours_StartBeforeWorkHours_WidensDayOne()
    {
        var calendar = ContractCalendar.Generate(
            At(Mon, 7), Mon, endTime: null, NineToFive, NoDates, new DailyFloorRule.None(), NoDates, NoPlan);

        calendar.DayOn(Mon).Should().Be(new ContractDay(Mon, At(Mon, 7), At(Mon, 17), null));
    }

    [Fact]
    public void Generate_WithWorkHours_NoWorkHoursDay_IsAFullDay()
    {
        var calendar = ContractCalendar.Generate(
            At(Mon, 9), Wed, endTime: null, NineToFive, [Tue, Wed], new DailyFloorRule.None(), NoDates, NoPlan);

        calendar.DayOn(Tue).Should().Be(FullDay(Tue));
        calendar.EndsAt.Should().Be(Midnight(Wed.AddDays(1)));
    }

    [Fact]
    public void Generate_WithWorkHours_AndAnEndTime_Throws()
    {
        var act = () => ContractCalendar.Generate(
            At(Mon, 9), Tue, endTime: new TimeOnly(12, 0), NineToFive, NoDates, new DailyFloorRule.None(), NoDates, NoPlan);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Generate_StartAfterDayOnesHoursEnd_Throws()
    {
        var act = () => ContractCalendar.Generate(
            At(Mon, 18), Tue, endTime: null, NineToFive, NoDates, new DailyFloorRule.None(), NoDates, NoPlan);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Generate_EndDateBeforeStartDate_Throws()
    {
        var act = () => ContractCalendar.Generate(
            At(Tue, 9), Mon, endTime: null, workHours: null, NoDates, new DailyFloorRule.None(), NoDates, NoPlan);

        act.Should().Throw<ArgumentException>();
    }

    // ---- Generate: floors ----

    [Fact]
    public void Generate_FixedFloor_AppliesToEveryDayExceptRestDays()
    {
        var calendar = ContractCalendar.Generate(
            At(Mon, 9), Wed, endTime: null, NineToFive, NoDates,
            new DailyFloorRule.Fixed(new DailyFloor(4)), [Tue], NoPlan);

        calendar.DayOn(Mon)!.Floor.Should().Be(new DailyFloor(4));
        calendar.DayOn(Tue)!.Floor.Should().BeNull();
        calendar.DayOn(Wed)!.Floor.Should().Be(new DailyFloor(4));
    }

    [Fact]
    public void Generate_FixedFloorLongerThanWhatIsLeftOfDayOne_IsRefused_NeverShrunk()
    {
        // 6h floor, contract starting 12:00 under 9–17 Work Hours → only 5h left on day 1.
        var act = () => ContractCalendar.Generate(
            At(Mon, 12), Tue, endTime: null, NineToFive, NoDates,
            new DailyFloorRule.Fixed(new DailyFloor(6)), NoDates, NoPlan);

        act.Should().Throw<ArgumentException>().WithMessage("Not enough time left on*6h floor*");
    }

    [Fact]
    public void Generate_FixedFloorThatStillFitsALateDayOne_KeepsTheFullFloor()
    {
        var calendar = ContractCalendar.Generate(
            At(Mon, 12), Tue, endTime: null, NineToFive, NoDates,
            new DailyFloorRule.Fixed(new DailyFloor(4)), NoDates, NoPlan);

        calendar.DayOn(Mon)!.Floor.Should().Be(new DailyFloor(4));
    }

    [Fact]
    public void Generate_LateDayOneThatIsARestDay_NeedsNoTime()
    {
        var calendar = ContractCalendar.Generate(
            At(Mon, 16), Tue, endTime: null, NineToFive, NoDates,
            new DailyFloorRule.Fixed(new DailyFloor(6)), [Mon], NoPlan);

        calendar.DayOn(Mon)!.IsRestDay.Should().BeTrue();
    }

    [Fact]
    public void Generate_FollowsPlan_EachDaysFloorIsItsPlannedHours_UnplannedDaysRest()
    {
        var calendar = ContractCalendar.Generate(
            At(Mon, 9), Wed, endTime: null, workHours: null, NoDates,
            new DailyFloorRule.FollowsPlan(), NoDates, new Dictionary<DateOnly, double> { [Mon] = 2, [Wed] = 3.5 });

        calendar.DayOn(Mon)!.Floor.Should().Be(new DailyFloor(2));
        calendar.DayOn(Tue)!.Floor.Should().BeNull();
        calendar.DayOn(Wed)!.Floor.Should().Be(new DailyFloor(3.5));
    }

    [Fact]
    public void Generate_RestDays_ApplyOnlyToAFixedFloor()
    {
        var calendar = ContractCalendar.Generate(
            At(Mon, 9), Tue, endTime: null, workHours: null, NoDates,
            new DailyFloorRule.FollowsPlan(), [Mon], new Dictionary<DateOnly, double> { [Mon] = 2 });

        calendar.DayOn(Mon)!.Floor.Should().Be(new DailyFloor(2));
    }

    [Fact]
    public void SuggestedStart_FutureDate_IsTheWorkHoursStart_OrMidnight()
    {
        var earlier = At(Mon.AddDays(-1), 20);

        ContractCalendar.SuggestedStart(Mon, NineToFive, NoDates, earlier).Should().Be(At(Mon, 9));
        ContractCalendar.SuggestedStart(Mon, NineToFive, [Mon], earlier).Should().Be(Midnight(Mon));
        ContractCalendar.SuggestedStart(Mon, null, NoDates, earlier).Should().Be(Midnight(Mon));
    }

    [Fact]
    public void SuggestedStart_Today_AfterTheDefaultStart_IsTheCurrentMinute()
    {
        var now = At(Mon, 12).AddSeconds(40);

        ContractCalendar.SuggestedStart(Mon, NineToFive, NoDates, now).Should().Be(At(Mon, 12));
    }

    [Fact]
    public void SuggestedStart_Today_BeforeTheDefaultStart_IsTheDefaultStart()
    {
        ContractCalendar.SuggestedStart(Mon, NineToFive, NoDates, At(Mon, 7)).Should().Be(At(Mon, 9));
    }

    [Fact]
    public void SuggestedStart_PastDate_KeepsItsDefault_SoSealCanRefuseIt()
    {
        ContractCalendar.SuggestedStart(Mon, NineToFive, NoDates, At(Tue, 10)).Should().Be(At(Mon, 9));
    }

    [Fact]
    public void Generate_None_LeavesEveryDayWithoutAFloor()
    {
        var calendar = ContractCalendar.Generate(
            At(Mon, 9), Tue, endTime: null, NineToFive, NoDates, new DailyFloorRule.None(), NoDates, NoPlan);

        calendar.HasAnyDailyFloor.Should().BeFalse();
    }
}
