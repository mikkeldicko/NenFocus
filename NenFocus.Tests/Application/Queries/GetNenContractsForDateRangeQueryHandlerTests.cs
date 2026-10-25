using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractDayStatuses;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Application.Queries.GetNenContractsForDateRange;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

/// <summary>
/// The Planner's view of the contracts touching a range. What each day owes and got is the
/// contract's own answer (NenContract.DayStatuses, Gap 95 — pinned in NenContractDayStatusTests),
/// stubbed here; this handler picks the contracts, maps the days for the badges and adds up the
/// banner from the same days.
/// </summary>
public class GetNenContractsForDateRangeQueryHandlerTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 10); // 10-day range
    private static readonly DateTime Today = new(2026, 1, 4, 12, 0, 0, DateTimeKind.Utc); // Day 4

    // Deliberately huge so tests that aren't about the range-filter itself always see
    // whatever contract(s) they set up, regardless of the contract's own date range.
    private static readonly GetNenContractsForDateRangeQuery AnyRange = new(new DateOnly(2000, 1, 1), new DateOnly(2100, 1, 1));

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> _deficits =
        Substitute.For<IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>>>();
    private readonly IQueryHandler<GetNenContractDayStatusesQuery, IReadOnlyList<ContractDayStatus>> _dayStatuses =
        Substitute.For<IQueryHandler<GetNenContractDayStatusesQuery, IReadOnlyList<ContractDayStatus>>>();
    private readonly ISystemClock _clock = TestClock.Create();

    public GetNenContractsForDateRangeQueryHandlerTests()
    {
        _clock.SetNow(Today);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _deficits.HandleAsync(Arg.Any<GetNenContractDeficitsQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[]);
        _dayStatuses.HandleAsync(Arg.Any<GetNenContractDayStatusesQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ContractDayStatus>)[]);
    }

    private GetNenContractsForDateRangeQueryHandler CreateHandler() => new(_contracts, _deficits, _dayStatuses, _clock);

    private void SetInEffect(params NenContract[] contracts) =>
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)contracts);

    private static NenContract Contract(
        DateTime? sealedAt = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        string name = "Study Month",
        StrictnessRule strictness = StrictnessRule.DailyFlexibility,
        double committedHours = 0)
    {
        var plan = new Dictionary<DateOnly, double>();
        if (committedHours > 0) plan[startDate ?? StartDate] = committedHours;
        return TestContracts.Seal(
            id: Guid.NewGuid(), name: name, goal: new NenContractGoal("Ship it"),
            strictnessRule: strictness,
            calendar: TestCalendars.For(startDate ?? StartDate, endDate ?? EndDate, new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            allowCancellation: false,
            plannedFocusHoursByDay: plan,
            sealedAt: At.Utc(sealedAt ?? (startDate ?? StartDate).ToDateTime(TimeOnly.MinValue)), timeZone: At.UtcZone);
    }

    private static ContractDayStatus Day(DateOnly date, double target, double done, double bonus = 0) =>
        new(date, target, done, bonus);

    private void StubDays(params ContractDayStatus[] days) =>
        _dayStatuses.HandleAsync(Arg.Any<GetNenContractDayStatusesQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ContractDayStatus>)days);

    /// <summary>Every day of Jan 1-10 owes 2h, except the rest days given; completed as given.</summary>
    private void StubTwoHoursADay(Dictionary<DateOnly, double> completed, params DateOnly[] restDays) =>
        StubDays(Enumerable.Range(0, 10).Select(i => StartDate.AddDays(i))
            .Select(date => Day(date, restDays.Contains(date) ? 0 : 2, completed.GetValueOrDefault(date))).ToArray());

    [Fact]
    public async Task HandleAsync_NoContractInEffect_ReturnsEmpty()
    {
        SetInEffect();

        var result = await CreateHandler().HandleAsync(AnyRange);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ContractInEffect_MapsNameAndDates()
    {
        var contract = Contract();
        SetInEffect(contract);

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.Id.Should().Be(contract.Id);
        result.Name.Should().Be("Study Month");
        result.StartDate.Should().Be(StartDate);
        result.EndDate.Should().Be(EndDate);
        result.TotalDays.Should().Be(10);
        result.DayNumber.Should().Be(4); // Today is Jan 4, contract starts Jan 1
        result.StrictnessRule.Should().Be(StrictnessRule.DailyFlexibility);
    }

    [Fact]
    public async Task HandleAsync_MapsStatus()
    {
        var contract = Contract(sealedAt: StartDate.ToDateTime(TimeOnly.MinValue)); // Active — sealed on/after its own StartDate
        SetInEffect(contract);

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.Status.Should().Be(NenContractStatus.Active);
    }

    [Fact]
    public async Task HandleAsync_DayNumber_UsesLocalCalendarDay_NotRawUtcDate()
    {
        // 1:00 AM UTC Jan 5 — but 8:00 PM local (UTC-5) on Jan 4. A fix that forgot to
        // convert would compute "today" as Jan 5 (DayNumber 5) instead of Jan 4 (DayNumber 4).
        var utcNow = new DateTime(2026, 1, 5, 1, 0, 0, DateTimeKind.Utc);
        _clock.SetNow(utcNow);
        _clock.SetTimeZoneOffset(TimeSpan.FromHours(-5));
        _clock.SetTimeZoneOffset(TimeSpan.FromHours(-5));
        var contract = Contract();
        SetInEffect(contract);

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.DayNumber.Should().Be(4);
    }

    [Fact]
    public async Task HandleAsync_TodayBeforeContractStart_DayNumberClampsToOne()
    {
        var futureStart = StartDate.AddDays(5);
        var contract = Contract(startDate: futureStart, endDate: futureStart.AddDays(9), sealedAt: StartDate.ToDateTime(TimeOnly.MinValue));
        SetInEffect(contract);

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.DayNumber.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_TodayAfterContractEnd_DayNumberClampsToTotalDays()
    {
        var pastRange = Contract(startDate: StartDate.AddDays(-20), endDate: StartDate.AddDays(-10));
        SetInEffect(pastRange);

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.DayNumber.Should().Be(result.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_EveryDayMet_DaysBehindIsZero()
    {
        SetInEffect(Contract());
        StubTwoHoursADay(new() { [StartDate] = 2, [StartDate.AddDays(1)] = 2, [StartDate.AddDays(2)] = 2, [StartDate.AddDays(3)] = 2 });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.DaysBehind.Should().Be(0);
        result.DaysConsidered.Should().Be(4); // Jan 1..4 inclusive (Today)
    }

    [Fact]
    public async Task HandleAsync_OneDayUnmet_CountsTowardDaysBehind()
    {
        SetInEffect(Contract());
        StubTwoHoursADay(new() { [StartDate] = 2, [StartDate.AddDays(1)] = 0, [StartDate.AddDays(2)] = 2, [StartDate.AddDays(3)] = 2 });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.DaysBehind.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_RestDay_DoesNotCountTowardDaysConsidered()
    {
        SetInEffect(Contract());
        StubTwoHoursADay(new(), StartDate.AddDays(1));

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.DaysConsidered.Should().Be(3); // Jan 1, 3, 4 — Jan 2 excluded as a rest day
        result.DaysBehind.Should().Be(3); // the three non-rest days are all unmet (0 hours logged)
    }

    [Fact]
    public async Task HandleAsync_SumsDeficitMinutesIntoHours()
    {
        var contract = Contract();
        SetInEffect(contract);
        _deficits.HandleAsync(Arg.Any<GetNenContractDeficitsQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)
            [
                new FocusBlockDeficitDto { FocusBlockId = Guid.NewGuid(), RemainingDeficit = WholeMinutes.Of(90) },
                new FocusBlockDeficitDto { FocusBlockId = Guid.NewGuid(), RemainingDeficit = WholeMinutes.Of(30) }
            ]);

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.Deficit.Should().Be(WholeMinutes.Of(120));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandleAsync_MapsAllowCancellation(bool allowCancellation)
    {
        var contract = TestContracts.Seal(
            id: Guid.NewGuid(),
            name: "Study Month",
            goal: new NenContractGoal("Ship it"),
            strictnessRule: StrictnessRule.DailyFlexibility,
            calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            allowCancellation: allowCancellation,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(StartDate.ToDateTime(TimeOnly.MinValue)), timeZone: At.UtcZone);
        SetInEffect(contract);

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.AllowCancellation.Should().Be(allowCancellation);
    }

    // ── Range-overlap filter + ordering ─────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_ContractDoesNotOverlapViewedRange_ReturnsEmpty()
    {
        // Contract is Jan 1-10; viewed range is Jan 20-26 — no shared days.
        var contract = Contract();
        SetInEffect(contract);

        var result = await CreateHandler().HandleAsync(new GetNenContractsForDateRangeQuery(new DateOnly(2026, 1, 20), new DateOnly(2026, 1, 26)));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ContractSharesOnlyOneBoundaryDayWithRange_StillOverlaps()
    {
        // Contract ends Jan 10; viewed range starts Jan 10 — exactly one shared day.
        var contract = Contract();
        SetInEffect(contract);

        var result = await CreateHandler().HandleAsync(new GetNenContractsForDateRangeQuery(EndDate, EndDate.AddDays(6)));

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task HandleAsync_TwoNonOverlappingContractsInSameRange_BothReturned_ActiveOrderedFirst()
    {
        // Reproduces the reported bug: a viewed range straddling the boundary between an
        // ending contract and the next one starting mid-range must surface both, not just
        // whichever used to win the old single-contract tiebreak.
        var scheduled = Contract(name: "Later Contract", startDate: new DateOnly(2026, 1, 8), endDate: new DateOnly(2026, 1, 14),
            sealedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)); // Scheduled (sealed before its own StartDate)
        var active = Contract(name: "Active Contract", startDate: StartDate, endDate: new DateOnly(2026, 1, 7),
            sealedAt: StartDate.ToDateTime(TimeOnly.MinValue)); // Active
        SetInEffect(scheduled, active);

        var result = await CreateHandler().HandleAsync(new GetNenContractsForDateRangeQuery(StartDate, new DateOnly(2026, 1, 14)));

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Active Contract"); // Active preferred first
        result[1].Name.Should().Be("Later Contract");
    }

    [Fact]
    public async Task HandleAsync_TwoScheduledContractsInSameRange_OrderedByEarlierStartDate()
    {
        var later = Contract(name: "Starts Later", startDate: new DateOnly(2026, 1, 8), endDate: new DateOnly(2026, 1, 14),
            sealedAt: new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        var earlier = Contract(name: "Starts Earlier", startDate: StartDate, endDate: new DateOnly(2026, 1, 7),
            sealedAt: new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        SetInEffect(later, earlier);

        var result = await CreateHandler().HandleAsync(new GetNenContractsForDateRangeQuery(StartDate, new DateOnly(2026, 1, 14)));

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Starts Earlier");
        result[1].Name.Should().Be("Starts Later");
    }

    // ── The badges: the contract's days, mapped ─────────────────────────────────

    [Fact]
    public async Task HandleAsync_AsksForTheDaysOfEachContract()
    {
        var contract = Contract();
        SetInEffect(contract);

        await CreateHandler().HandleAsync(AnyRange);

        await _dayStatuses.Received(1).HandleAsync(new GetNenContractDayStatusesQuery(contract.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_APastDay_ShowsWhatWasDoneAgainstWhatItOwes()
    {
        SetInEffect(Contract());
        StubDays(Day(StartDate.AddDays(1), target: 2, done: 1.5, bonus: 0.5));

        var day = (await CreateHandler().HandleAsync(AnyRange)).Single().DayStatuses.Single();

        day.Should().BeEquivalentTo(new NenContractDayStatusDto
        {
            Date = StartDate.AddDays(1), CurrentHours = 1.5, TargetHours = 2, IsMet = false, IsRestDay = false,
            Timing = DayTiming.Past, BonusHours = 0.5
        });
    }

    [Fact]
    public async Task HandleAsync_Today_ShowsWhatWasDoneSoFar()
    {
        SetInEffect(Contract());
        StubDays(Day(StartDate.AddDays(3), target: 2, done: 2));

        var day = (await CreateHandler().HandleAsync(AnyRange)).Single().DayStatuses.Single();

        day.Timing.Should().Be(DayTiming.Today);
        day.CurrentHours.Should().Be(2);
        day.IsMet.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_AFutureDay_ShowsWhatItOwes_AsPlanned_WithNoBonusYet()
    {
        // By the one day rule a future day's plan is exactly what it owes, so it reads as met.
        SetInEffect(Contract());
        StubDays(Day(StartDate.AddDays(6), target: 3, done: 0, bonus: 1));

        var day = (await CreateHandler().HandleAsync(AnyRange)).Single().DayStatuses.Single();

        day.Timing.Should().Be(DayTiming.Future);
        day.CurrentHours.Should().Be(3);
        day.TargetHours.Should().Be(3);
        day.IsMet.Should().BeTrue();
        day.BonusHours.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ARestDay_IsMappedAsRest()
    {
        SetInEffect(Contract());
        StubDays(Day(StartDate.AddDays(1), target: 0, done: 0));

        var day = (await CreateHandler().HandleAsync(AnyRange)).Single().DayStatuses.Single();

        day.IsRestDay.Should().BeTrue();
        day.IsMet.Should().BeTrue();
        day.TargetHours.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_TheBadgeAndTheBanner_GiveTheSameVerdictForTheSameDay()
    {
        // Gap 95 finding 3: the badge and the banner used to answer "was this day met" by two
        // different rules. Both now read the same day.
        SetInEffect(Contract());
        StubDays(Day(StartDate, target: 2, done: 2), Day(StartDate.AddDays(1), target: 2, done: 1));

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        var unmetBadges = result.DayStatuses.Count(d => d.Timing != DayTiming.Future && !d.IsRestDay && !d.IsMet);
        result.DaysBehind.Should().Be(unmetBadges).And.Be(1);
        result.DayStatuses.Single(d => d.Date == StartDate.AddDays(1)).IsMet.Should().BeFalse();
    }

    // ── The banner: totals over the same days ───────────────────────────────────

    [Fact]
    public async Task HandleAsync_Totals_AddUpTheDays_AgainstWhatWasCommittedAtSeal()
    {
        SetInEffect(Contract(committedHours: 6));
        StubDays(Day(StartDate, 2, 2, bonus: 1), Day(StartDate.AddDays(1), 2, 1.5), Day(StartDate.AddDays(6), 2, 0, bonus: 0.5));

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.TotalCommittedHours.Should().Be(6);
        result.TotalCompletedHours.Should().Be(3.5);
        result.TotalBonusHours.Should().Be(1.5);
    }

    [Theory]
    [InlineData(StrictnessRule.PeriodTimeBudget, true)]
    [InlineData(StrictnessRule.DailyFlexibility, false)]
    public async Task HandleAsync_MapsWhetherTheContractHasAPeriodTarget(StrictnessRule strictness, bool expected)
    {
        SetInEffect(Contract(strictness: strictness));

        (await CreateHandler().HandleAsync(AnyRange)).Single().HasPeriodTarget.Should().Be(expected);
    }
}
