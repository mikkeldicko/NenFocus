using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Application.Queries.GetNenContractBonusFocusHours;
using NenFocus.Application.Queries.GetNenContractCompletedFocusHours;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Application.Queries.GetNenContractsForDateRange;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

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
    private readonly IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _completedHours =
        Substitute.For<IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>>();
    private readonly IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _plannedHours =
        Substitute.For<IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>>();
    private readonly IQueryHandler<GetNenContractBonusFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _bonusHours =
        Substitute.For<IQueryHandler<GetNenContractBonusFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IFocusBlockOutcomeClassifier _classifier = Substitute.For<IFocusBlockOutcomeClassifier>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public GetNenContractsForDateRangeQueryHandlerTests()
    {
        _clock.UtcNow.Returns(Today);
        _clock.ToLocal(Arg.Any<DateTime>()).Returns(ci => ci.Arg<DateTime>());
        _deficits.HandleAsync(Arg.Any<GetNenContractDeficitsQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[]);
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>());
        _plannedHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>());
        _bonusHours.HandleAsync(Arg.Any<GetNenContractBonusFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>());
        _blocks.GetForContractAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _sessions.GetByOriginatingBlockIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Session?)null);
    }

    private GetNenContractsForDateRangeQueryHandler CreateHandler() =>
        new(_contracts, _deficits, _completedHours, _plannedHours, _bonusHours, _blocks, _sessions, _classifier, _clock);

    private void SetInEffect(params NenContract[] contracts) =>
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)contracts);

    private static NenContract Contract(
        DailyFloorRule? floorRule = null,
        IReadOnlyDictionary<DateOnly, double>? plannedFocusHoursByDay = null,
        DateTime? sealedAt = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        string name = "Study Month",
        IReadOnlyCollection<DateOnly>? restDays = null)
    {
        var rule = floorRule ?? new DailyFloorRule.None();
        var plan = plannedFocusHoursByDay ?? new Dictionary<DateOnly, double>();
        return NenContract.Seal(
            id: Guid.NewGuid(), name: name, goal: new NenContractGoal("Ship it"),
            strictnessRule: StrictnessRule.DailyFlexibility,
            calendar: TestCalendars.For(startDate ?? StartDate, endDate ?? EndDate, rule, plan, restDays: restDays),
            floorRule: rule,
            changeWindow: null, allowCancellation: false,
            plannedFocusHoursByDay: plan,
            localSealedAt: sealedAt ?? (startDate ?? StartDate).ToDateTime(TimeOnly.MinValue));
    }

    private static Dictionary<DateOnly, double> FullyPlanned(DateOnly start, DateOnly end, double hoursPerDay)
    {
        var map = new Dictionary<DateOnly, double>();
        for (var date = start; date <= end; date = date.AddDays(1))
            map[date] = hoursPerDay;
        return map;
    }

    // CommittedFocusHours is computed from this same map at Seal — matching real usage
    // (SealNenContractCommandHandler derives ContractFloor from the identical sum), so the
    // two stay numerically consistent here too.
    private static NenContract ContractFloorContract(double contractFloorHours) =>
        NenContract.Seal(
            id: Guid.NewGuid(),
            name: "Period Contract",
            goal: new NenContractGoal("Ship it"),
            strictnessRule: StrictnessRule.PeriodTimeBudget,
            calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [StartDate] = contractFloorHours },
            localSealedAt: StartDate.ToDateTime(TimeOnly.MinValue),
            contractFloor: new ContractFloor(contractFloorHours));

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
        _clock.UtcNow.Returns(utcNow);
        _clock.ToLocal(Arg.Any<DateTime>()).Returns(ci => ci.Arg<DateTime>().AddHours(-5));
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
        var contract = Contract(
            floorRule: new DailyFloorRule.Fixed(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>
            {
                [StartDate] = 2, [StartDate.AddDays(1)] = 2, [StartDate.AddDays(2)] = 2, [StartDate.AddDays(3)] = 2
            });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.DaysBehind.Should().Be(0);
        result.DaysConsidered.Should().Be(4); // Jan 1..4 inclusive (Today)
    }

    [Fact]
    public async Task HandleAsync_OneDayUnmet_CountsTowardDaysBehind()
    {
        var contract = Contract(
            floorRule: new DailyFloorRule.Fixed(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>
            {
                [StartDate] = 2, [StartDate.AddDays(1)] = 0, [StartDate.AddDays(2)] = 2, [StartDate.AddDays(3)] = 2
            });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.DaysBehind.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_RestDay_DoesNotCountTowardDaysConsidered()
    {
        var restDay = StartDate.AddDays(1);
        var contract = Contract(floorRule: new DailyFloorRule.Fixed(new DailyFloor(2)), restDays: [restDay], plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);
        // No completed hours anywhere — the rest day must still not count as "behind".
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
                new FocusBlockDeficitDto { FocusBlockId = Guid.NewGuid(), RemainingDeficitMinutes = 90 },
                new FocusBlockDeficitDto { FocusBlockId = Guid.NewGuid(), RemainingDeficitMinutes = 30 }
            ]);

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.DeficitHours.Should().Be(2.0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandleAsync_MapsAllowCancellation(bool allowCancellation)
    {
        var contract = NenContract.Seal(
            id: Guid.NewGuid(),
            name: "Study Month",
            goal: new NenContractGoal("Ship it"),
            strictnessRule: StrictnessRule.DailyFlexibility,
            calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            changeWindow: null,
            allowCancellation: allowCancellation,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            localSealedAt: StartDate.ToDateTime(TimeOnly.MinValue));
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

    // ── DayStatuses (per-day floor badges) ──────────────────────────────────────

    [Fact]
    public async Task HandleAsync_DayStatuses_CoversFullContractRange_NotJustPastAndToday()
    {
        // Today is Jan 4; contract runs Jan 1-10 — DayStatuses must include every day,
        // unlike DaysBehind/DaysConsidered which stop at today.
        var contract = Contract(
            floorRule: new DailyFloorRule.Fixed(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.DayStatuses.Should().HaveCount(10);
        result.DayStatuses.Select(d => d.Date).Should().Contain(EndDate); // Jan 10, well after "today"
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_PastDay_UsesCompletedHours()
    {
        var contract = Contract(
            floorRule: new DailyFloorRule.Fixed(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [StartDate] = 2 });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        var day = result.DayStatuses.Single(d => d.Date == StartDate);
        day.CurrentHours.Should().Be(2);
        day.FloorHours.Should().Be(2);
        day.IsMet.Should().BeTrue();
        day.IsRestDay.Should().BeFalse();
        day.Timing.Should().Be(DayTiming.Past);
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_PastDayBelowFloor_IsNotMet()
    {
        var contract = Contract(
            floorRule: new DailyFloorRule.Fixed(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [StartDate] = 1 });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.DayStatuses.Single(d => d.Date == StartDate).IsMet.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_FutureDay_UsesPlannedHoursNotCompletedHours()
    {
        // Today is Jan 4; Jan 6 is a future day within the contract.
        var futureDate = StartDate.AddDays(5);
        var contract = Contract(
            floorRule: new DailyFloorRule.Fixed(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [futureDate] = 0 });
        _plannedHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [futureDate] = 3 });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        var day = result.DayStatuses.Single(d => d.Date == futureDate);
        day.CurrentHours.Should().Be(3); // planned, not the 0 completed hours
        day.IsMet.Should().BeTrue();
        day.Timing.Should().Be(DayTiming.Future);
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_Today_IsMarkedTimingToday()
    {
        var contract = Contract(
            floorRule: new DailyFloorRule.Fixed(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        var day = result.DayStatuses.Single(d => d.Date == DateOnly.FromDateTime(Today));
        day.Timing.Should().Be(DayTiming.Today);
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_PastDay_IsMarkedTimingPast()
    {
        var contract = Contract(
            floorRule: new DailyFloorRule.Fixed(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        var day = result.DayStatuses.Single(d => d.Date == StartDate); // Jan 1, today is Jan 4
        day.Timing.Should().Be(DayTiming.Past);
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_RestDay_IsMetTrueWithZeroFloor()
    {
        var restDay = StartDate.AddDays(1);
        var contract = Contract(floorRule: new DailyFloorRule.Fixed(new DailyFloor(2)), restDays: [restDay], plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        var day = result.DayStatuses.Single(d => d.Date == restDay);
        day.IsRestDay.Should().BeTrue();
        day.IsMet.Should().BeTrue();
        day.FloorHours.Should().Be(0);
        day.CurrentHours.Should().Be(0);
    }

    // ── Live daily-floor badges: ad-hoc floor for an originally-rest day, pinned floor
    // for a committed day, and a genuinely separate Bonus number (Open Gap L, piece 1) ──

    [Fact]
    public async Task HandleAsync_DayStatuses_TrueRestDay_NothingScheduled_StaysRest()
    {
        var restDay = StartDate.AddDays(1);
        var contract = Contract(floorRule: new DailyFloorRule.Fixed(new DailyFloor(2)), restDays: [restDay], plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        var day = result.DayStatuses.Single(d => d.Date == restDay);
        day.IsRestDay.Should().BeTrue();
        day.IsMet.Should().BeTrue();
        day.FloorHours.Should().Be(0);
        day.CurrentHours.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_OriginallyRestDay_RedemptionLandsThere_GetsAdHocFloorNoLongerRest()
    {
        // A redemption (or any committed block moved in under Period Time Budget) lands
        // on a day with no committed floor — it stops reading as "Rest" and gets an
        // ad-hoc target equal to its own currently-scheduled duration, fully met once
        // that duration is completed.
        var restDay = StartDate.AddDays(1);
        var contract = Contract(floorRule: new DailyFloorRule.Fixed(new DailyFloor(2)), restDays: [restDay], plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);
        _plannedHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [restDay] = 1.0 });
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [restDay] = 1.0 });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        var day = result.DayStatuses.Single(d => d.Date == restDay);
        day.IsRestDay.Should().BeFalse();
        day.FloorHours.Should().Be(1.0);
        day.CurrentHours.Should().Be(1.0);
        day.IsMet.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_OriginallyRestDay_ScheduledNotYetCompleted_AdHocFloorNotMet()
    {
        var restDay = StartDate.AddDays(1);
        var contract = Contract(floorRule: new DailyFloorRule.Fixed(new DailyFloor(2)), restDays: [restDay], plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);
        _plannedHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [restDay] = 1.0 });
        // completedHours defaults to empty — nothing done there yet.

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        var day = result.DayStatuses.Single(d => d.Date == restDay);
        day.IsRestDay.Should().BeFalse();
        day.FloorHours.Should().Be(1.0);
        day.CurrentHours.Should().Be(0);
        day.IsMet.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_CommittedFloorDay_SurplusHours_FloorStaysPinned()
    {
        // A real committed floor never becomes "whatever got scheduled" — surplus hours
        // show as visible over-delivery against the same pinned target.
        var contract = Contract(floorRule: new DailyFloorRule.Fixed(new DailyFloor(1)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 3));
        SetInEffect(contract);
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [StartDate] = 3.0 });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        var day = result.DayStatuses.Single(d => d.Date == StartDate);
        day.FloorHours.Should().Be(1.0);
        day.CurrentHours.Should().Be(3.0);
        day.IsMet.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_BonusHours_NeverCountTowardFloorOrCurrentHours()
    {
        var restDay = StartDate.AddDays(1);
        var contract = Contract(floorRule: new DailyFloorRule.Fixed(new DailyFloor(2)), restDays: [restDay], plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);
        _bonusHours.HandleAsync(Arg.Any<GetNenContractBonusFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [restDay] = 2.5 });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        var day = result.DayStatuses.Single(d => d.Date == restDay);
        day.BonusHours.Should().Be(2.5);
        day.CurrentHours.Should().Be(0); // Bonus never enters the floor badge's numerator
        day.IsRestDay.Should().BeTrue(); // Bonus alone never un-rests a day
        day.FloorHours.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_TotalFloorHours_NoDailyFloorsAtAll_StillReflectsWhatWasPlannedAtSeal()
    {
        // Reproduces the second reported bug: a contract with no daily floors (no
        // per-day floor anywhere — exactly what the debug seed fixtures use) still had a
        // real 1h block planned at Seal. TotalFloorHours must be 1h, not 0 — it was 0
        // under the previous "sum only days with a floor" approach, since
        // no day has a floor regardless of
        // what was actually planned.
        var missedDay = StartDate;
        var contract = Contract(floorRule: new DailyFloorRule.None(),
            plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [missedDay] = 1.0 });
        SetInEffect(contract);
        // The block is still scheduled there (missed, not deleted) — ComputePlannedFocusHoursQuery
        // still reports its scheduled duration; nothing was ever completed.
        _plannedHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [missedDay] = 1.0 });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.TotalFloorHours.Should().Be(1.0);
        result.TotalCompletedHours.Should().Be(0.0);
    }

    [Fact]
    public async Task HandleAsync_TotalFloorHours_RedemptionOnAdHocDay_DoesNotDoubleCountTheRelocatedHour()
    {
        // Reproduces the reported bug: 1h was committed at Seal (StartDate). It was missed,
        // then fully redeemed by a 1h block on a day that had no committed floor of its own
        // (StartDate + 1). TotalFloorHours must stay 1h (the real commitment) — not 2h from
        // also counting the redemption's ad-hoc floor on top of it.
        var committedDay = StartDate;
        var redemptionDay = StartDate.AddDays(1);
        var contract = Contract(floorRule: new DailyFloorRule.FollowsPlan(), plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [committedDay] = 1.0 });
        SetInEffect(contract);
        // Missed day: nothing completed. Redemption day: 1h scheduled and completed there instead.
        _plannedHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [redemptionDay] = 1.0 });
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [redemptionDay] = 1.0 });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.TotalFloorHours.Should().Be(1.0);
        result.TotalCompletedHours.Should().Be(1.0);
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_TwoSameDayRedemptionAttemptsForSameMissedBlock_FloorCapsAtOriginalDeficit()
    {
        // Reproduces the reported bug directly: a redemption block ends early, a second
        // redemption block on the *same day* covers the remaining deficit and completes.
        // The floor badge must cap at the missed block's own original 1h deficit (1h/1h),
        // not sum both attempts' raw scheduled spans (1h/2h) — the first attempt's stale,
        // superseded claim must not count twice alongside the second attempt's claim.
        var missedDay = StartDate;
        var redemptionDay = StartDate.AddDays(1);
        var contract = Contract(floorRule: new DailyFloorRule.None());
        SetInEffect(contract);

        var missed = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            missedDay.ToDateTime(new TimeOnly(9, 0)), missedDay.ToDateTime(new TimeOnly(10, 0)), contractId: contract.Id);
        // First attempt: claimed the full 60-minute deficit, ended early after 20 minutes.
        var attempt1 = FocusBlock.Create(Guid.NewGuid(), missed.BlockPresetId,
            redemptionDay.ToDateTime(new TimeOnly(9, 0)), redemptionDay.ToDateTime(new TimeOnly(10, 0)),
            contractId: contract.Id, redemptionEntries: [new RedemptionEntry(missed.Id, 60)]);
        // Second attempt, same day: claims exactly what's left after the first attempt's
        // real 20-minute credit (40 minutes), and completes it fully.
        var attempt2 = FocusBlock.Create(Guid.NewGuid(), missed.BlockPresetId,
            redemptionDay.ToDateTime(new TimeOnly(11, 0)), redemptionDay.ToDateTime(new TimeOnly(11, 40)),
            contractId: contract.Id, redemptionEntries: [new RedemptionEntry(missed.Id, 40)]);

        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[missed, attempt1, attempt2]);
        // The missed block itself never started — nothing credited, its full 60 minutes
        // stays owed forever once settled.
        _classifier.CreditedFocusMinutes(missed.Start, missed.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<double>())
            .Returns(0);
        // The raw (buggy) scheduled-span sum a naive read would report for redemptionDay:
        // attempt1's full 1h claim plus attempt2's 40-minute claim = 1h40m.
        _plannedHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [redemptionDay] = 1.0 + 40.0 / 60.0 });
        // Completed hours (real, credited): 20 min from attempt1 + 40 min from attempt2 = 1h.
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [redemptionDay] = 1.0 });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        var day = result.DayStatuses.Single(d => d.Date == redemptionDay);
        day.FloorHours.Should().BeApproximately(1.0, 1e-9);
        day.CurrentHours.Should().Be(1.0);
        day.IsMet.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_RedemptionRetryOnADifferentDay_DoesNotReduceEitherDaysFloor()
    {
        // A retry on a *different* day must not retroactively shrink the original day's
        // floor, and the original day's own claim must not reduce the new day's floor
        // either — each day keeps its own historically-scheduled target, unlike the
        // same-day case above where the two attempts share one day's badge.
        var missedDay = StartDate;
        var firstAttemptDay = StartDate.AddDays(1);
        var secondAttemptDay = StartDate.AddDays(2);
        var contract = Contract(floorRule: new DailyFloorRule.None());
        SetInEffect(contract);

        var missed = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            missedDay.ToDateTime(new TimeOnly(9, 0)), missedDay.ToDateTime(new TimeOnly(10, 0)), contractId: contract.Id);
        var attempt1 = FocusBlock.Create(Guid.NewGuid(), missed.BlockPresetId,
            firstAttemptDay.ToDateTime(new TimeOnly(9, 0)), firstAttemptDay.ToDateTime(new TimeOnly(10, 0)),
            contractId: contract.Id, redemptionEntries: [new RedemptionEntry(missed.Id, 60)]);
        var attempt2 = FocusBlock.Create(Guid.NewGuid(), missed.BlockPresetId,
            secondAttemptDay.ToDateTime(new TimeOnly(9, 0)), secondAttemptDay.ToDateTime(new TimeOnly(9, 30)),
            contractId: contract.Id, redemptionEntries: [new RedemptionEntry(missed.Id, 30)]);

        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[missed, attempt1, attempt2]);
        _classifier.CreditedFocusMinutes(missed.Start, missed.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<double>())
            .Returns(0);
        _plannedHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>
            {
                [firstAttemptDay] = 1.0,
                [secondAttemptDay] = 0.5
            });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.DayStatuses.Single(d => d.Date == firstAttemptDay).FloorHours.Should().Be(1.0);
        result.DayStatuses.Single(d => d.Date == secondAttemptDay).FloorHours.Should().Be(0.5);
    }

    [Fact]
    public async Task HandleAsync_TotalBonusHours_SumsAcrossContract()
    {
        var contract = Contract();
        SetInEffect(contract);
        _bonusHours.HandleAsync(Arg.Any<GetNenContractBonusFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>
            {
                [StartDate] = 1.5,
                [StartDate.AddDays(2)] = 0.5
            });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.TotalBonusHours.Should().Be(2.0);
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_ResolvesFloorHoursPerDay()
    {
        var contract = Contract(
            floorRule: new DailyFloorRule.FollowsPlan(),
            plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [StartDate] = 2, [StartDate.AddDays(1)] = 5 });
        SetInEffect(contract);

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.DayStatuses.Single(d => d.Date == StartDate).FloorHours.Should().Be(2);
        result.DayStatuses.Single(d => d.Date == StartDate.AddDays(1)).FloorHours.Should().Be(5);
    }

    // ── ContractFloor mode (Period Time Budget, no per-day floor) ──────────────────

    [Fact]
    public async Task HandleAsync_OrdinaryContract_ContractFloorHoursIsNull()
    {
        var contract = Contract();
        SetInEffect(contract);

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.ContractFloorHours.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ContractFloorMode_MapsTargetAndTotalCompletedHours()
    {
        SetInEffect(ContractFloorContract(9));
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>
            {
                [StartDate] = 3, [StartDate.AddDays(1)] = 1.5
            });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.ContractFloorHours.Should().Be(9);
        result.TotalFloorHours.Should().Be(9);
        result.TotalCompletedHours.Should().Be(4.5);
    }

    [Fact]
    public async Task HandleAsync_OrdinaryContract_TotalFloorHoursSumsEveryDaysFloor()
    {
        var contract = Contract(
            floorRule: new DailyFloorRule.Fixed(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.TotalFloorHours.Should().Be(20); // 2h * 10 days
    }

    [Fact]
    public async Task HandleAsync_OrdinaryContract_TotalCompletedHoursSumsCompletedAcrossAllDays()
    {
        var contract = Contract(
            floorRule: new DailyFloorRule.Fixed(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>
            {
                [StartDate] = 2, [StartDate.AddDays(1)] = 1.5
            });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.TotalCompletedHours.Should().Be(3.5);
    }

    [Fact]
    public async Task HandleAsync_ContractFloorMode_NoDayIsEverRestDay()
    {
        // Nothing planned or completed anywhere — an ordinary contract would resolve
        // every day to a rest day (no floor); ContractFloor mode never does.
        SetInEffect(ContractFloorContract(9));

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.DayStatuses.Should().OnlyContain(d => !d.IsRestDay);
    }

    [Fact]
    public async Task HandleAsync_ContractFloorMode_PastDayFullyCompleted_IsMet()
    {
        SetInEffect(ContractFloorContract(9));
        _plannedHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [StartDate] = 2 });
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [StartDate] = 2 });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        var day = result.DayStatuses.Single(d => d.Date == StartDate);
        day.CurrentHours.Should().Be(2);
        day.FloorHours.Should().Be(2); // that day's planned hours, not a real floor
        day.IsMet.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ContractFloorMode_PastDayPartiallyMissed_IsNotMet()
    {
        SetInEffect(ContractFloorContract(9));
        _plannedHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [StartDate] = 2 });
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [StartDate] = 1 });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        var day = result.DayStatuses.Single(d => d.Date == StartDate);
        day.CurrentHours.Should().Be(1);
        day.IsMet.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ContractFloorMode_DayWithNothingPlannedOrCompleted_StillMet()
    {
        // A day emptied on purpose (hours moved elsewhere) shows as met, not a failure —
        // 0 completed against 0 planned is trivially satisfied.
        SetInEffect(ContractFloorContract(9));

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        var day = result.DayStatuses.Single(d => d.Date == StartDate);
        day.CurrentHours.Should().Be(0);
        day.FloorHours.Should().Be(0);
        day.IsMet.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ContractFloorMode_FutureDay_UsesPlannedHours()
    {
        var futureDate = StartDate.AddDays(5); // today is Jan 4
        SetInEffect(ContractFloorContract(9));
        _plannedHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [futureDate] = 4 });

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        var day = result.DayStatuses.Single(d => d.Date == futureDate);
        day.CurrentHours.Should().Be(4);
        day.FloorHours.Should().Be(4);
        day.Timing.Should().Be(DayTiming.Future);
    }

    [Fact]
    public async Task HandleAsync_ContractFloorMode_DaysBehindAndDaysConsideredAreZero()
    {
        // Day-Met scoring is fully removed for ContractFloor contracts — the days-behind
        // loop naturally yields zero since every day resolves to "no per-day obligation."
        SetInEffect(ContractFloorContract(9));

        var result = (await CreateHandler().HandleAsync(AnyRange)).Single();

        result.DaysBehind.Should().Be(0);
        result.DaysConsidered.Should().Be(0);
    }
}
