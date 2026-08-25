using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputePlannedFocusHours;
using NenFocus.Application.Queries.GetActiveNenContract;
using NenFocus.Application.Queries.GetNenContractCompletedFocusHours;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetActiveNenContractQueryHandlerTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 10); // 10-day range
    private static readonly DateTime Today = new(2026, 1, 4, 12, 0, 0, DateTimeKind.Utc); // Day 4

    // Deliberately huge so tests that aren't about the week-filter itself always see
    // whatever contract(s) they set up, regardless of the contract's own date range.
    private static readonly GetActiveNenContractQuery AnyWeek = new(new DateOnly(2000, 1, 1), new DateOnly(2100, 1, 1));

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> _deficits =
        Substitute.For<IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>>>();
    private readonly IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _completedHours =
        Substitute.For<IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>>();
    private readonly IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _plannedHours =
        Substitute.For<IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public GetActiveNenContractQueryHandlerTests()
    {
        _clock.UtcNow.Returns(Today);
        _clock.ToLocal(Arg.Any<DateTime>()).Returns(ci => ci.Arg<DateTime>());
        _deficits.HandleAsync(Arg.Any<GetNenContractDeficitsQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[]);
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>());
        _plannedHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>());
    }

    private GetActiveNenContractQueryHandler CreateHandler() => new(_contracts, _deficits, _completedHours, _plannedHours, _clock);

    private void SetInEffect(params NenContract[] contracts) =>
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)contracts);

    private static NenContract Contract(
        DailyFloorPolicy? dailyFloorPolicy = null,
        IReadOnlyDictionary<DateOnly, double>? plannedFocusHoursByDay = null,
        DateTime? sealedAt = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        string name = "Study Month") =>
        NenContract.Seal(
            Guid.NewGuid(), name, new NenContractGoal("Ship it"),
            startDate ?? StartDate, endDate ?? EndDate,
            StrictnessRule.DailyFlexibility,
            dailyFloorPolicy ?? DailyFloorPolicy.None,
            null, false,
            plannedFocusHoursByDay ?? new Dictionary<DateOnly, double>(),
            sealedAt ?? (startDate ?? StartDate).ToDateTime(TimeOnly.MinValue));

    private static Dictionary<DateOnly, double> FullyPlanned(DateOnly start, DateOnly end, double hoursPerDay)
    {
        var map = new Dictionary<DateOnly, double>();
        for (var date = start; date <= end; date = date.AddDays(1))
            map[date] = hoursPerDay;
        return map;
    }

    private static NenContract ContractFloorContract(double contractFloorHours) =>
        NenContract.Seal(
            Guid.NewGuid(), "Period Contract", new NenContractGoal("Ship it"),
            StartDate, EndDate, StrictnessRule.PeriodTimeBudget, DailyFloorPolicy.None,
            null, false, new Dictionary<DateOnly, double>(), StartDate.ToDateTime(TimeOnly.MinValue),
            contractFloor: new ContractFloor(contractFloorHours));

    [Fact]
    public async Task HandleAsync_NoContractInEffect_ReturnsNull()
    {
        SetInEffect();

        var result = await CreateHandler().HandleAsync(AnyWeek);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ContractInEffect_MapsNameAndDates()
    {
        var contract = Contract();
        SetInEffect(contract);

        var result = await CreateHandler().HandleAsync(AnyWeek);

        result!.Id.Should().Be(contract.Id);
        result.Name.Should().Be("Study Month");
        result.StartDate.Should().Be(StartDate);
        result.EndDate.Should().Be(EndDate);
        result.TotalDays.Should().Be(10);
        result.DayNumber.Should().Be(4); // Today is Jan 4, contract starts Jan 1
        result.StrictnessRule.Should().Be(StrictnessRule.DailyFlexibility);
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

        var result = await CreateHandler().HandleAsync(AnyWeek);

        result!.DayNumber.Should().Be(4);
    }

    [Fact]
    public async Task HandleAsync_TodayBeforeContractStart_DayNumberClampsToOne()
    {
        var futureStart = StartDate.AddDays(5);
        var contract = Contract(startDate: futureStart, endDate: futureStart.AddDays(9), sealedAt: StartDate.ToDateTime(TimeOnly.MinValue));
        SetInEffect(contract);

        var result = await CreateHandler().HandleAsync(AnyWeek);

        result!.DayNumber.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_TodayAfterContractEnd_DayNumberClampsToTotalDays()
    {
        var pastRange = Contract(startDate: StartDate.AddDays(-20), endDate: StartDate.AddDays(-10));
        SetInEffect(pastRange);

        var result = await CreateHandler().HandleAsync(AnyWeek);

        result!.DayNumber.Should().Be(result.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_EveryDayMet_DaysBehindIsZero()
    {
        var contract = Contract(
            dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>
            {
                [StartDate] = 2, [StartDate.AddDays(1)] = 2, [StartDate.AddDays(2)] = 2, [StartDate.AddDays(3)] = 2
            });

        var result = await CreateHandler().HandleAsync(AnyWeek);

        result!.DaysBehind.Should().Be(0);
        result.DaysConsidered.Should().Be(4); // Jan 1..4 inclusive (Today)
    }

    [Fact]
    public async Task HandleAsync_OneDayUnmet_CountsTowardDaysBehind()
    {
        var contract = Contract(
            dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>
            {
                [StartDate] = 2, [StartDate.AddDays(1)] = 0, [StartDate.AddDays(2)] = 2, [StartDate.AddDays(3)] = 2
            });

        var result = await CreateHandler().HandleAsync(AnyWeek);

        result!.DaysBehind.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_RestDay_DoesNotCountTowardDaysConsidered()
    {
        var restDay = StartDate.AddDays(1);
        var policy = new DailyFloorPolicy(new DailyFloor(2), new Dictionary<DateOnly, DailyFloor?> { [restDay] = null });
        var contract = Contract(dailyFloorPolicy: policy, plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);
        // No completed hours anywhere — the rest day must still not count as "behind".
        var result = await CreateHandler().HandleAsync(AnyWeek);

        result!.DaysConsidered.Should().Be(3); // Jan 1, 3, 4 — Jan 2 excluded as a rest day
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

        var result = await CreateHandler().HandleAsync(AnyWeek);

        result!.DeficitHours.Should().Be(2.0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandleAsync_MapsAllowCancellation(bool allowCancellation)
    {
        var contract = NenContract.Seal(
            Guid.NewGuid(), "Study Month", new NenContractGoal("Ship it"),
            StartDate, EndDate, StrictnessRule.DailyFlexibility, DailyFloorPolicy.None, null,
            allowCancellation, new Dictionary<DateOnly, double>(), StartDate.ToDateTime(TimeOnly.MinValue));
        SetInEffect(contract);

        var result = await CreateHandler().HandleAsync(AnyWeek);

        result!.AllowCancellation.Should().Be(allowCancellation);
    }

    // ── Week-overlap filter + tiebreak ──────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_ContractDoesNotOverlapViewedWeek_ReturnsNull()
    {
        // Contract is Jan 1-10; viewed week is Jan 20-26 — no shared days.
        var contract = Contract();
        SetInEffect(contract);

        var result = await CreateHandler().HandleAsync(new GetActiveNenContractQuery(new DateOnly(2026, 1, 20), new DateOnly(2026, 1, 26)));

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ContractSharesOnlyOneBoundaryDayWithWeek_StillOverlaps()
    {
        // Contract ends Jan 10; viewed week starts Jan 10 — exactly one shared day.
        var contract = Contract();
        SetInEffect(contract);

        var result = await CreateHandler().HandleAsync(new GetActiveNenContractQuery(EndDate, EndDate.AddDays(6)));

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_TwoNonOverlappingContractsInSameWeek_PrefersActiveOverScheduled()
    {
        var scheduled = Contract(name: "Later Contract", startDate: new DateOnly(2026, 1, 8), endDate: new DateOnly(2026, 1, 14),
            sealedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)); // Scheduled (sealed before its own StartDate)
        var active = Contract(name: "Active Contract", startDate: StartDate, endDate: new DateOnly(2026, 1, 7),
            sealedAt: StartDate.ToDateTime(TimeOnly.MinValue)); // Active
        SetInEffect(scheduled, active);

        var result = await CreateHandler().HandleAsync(new GetActiveNenContractQuery(StartDate, new DateOnly(2026, 1, 14)));

        result!.Name.Should().Be("Active Contract");
    }

    [Fact]
    public async Task HandleAsync_TwoScheduledContractsInSameWeek_PrefersEarlierStartDate()
    {
        var later = Contract(name: "Starts Later", startDate: new DateOnly(2026, 1, 8), endDate: new DateOnly(2026, 1, 14),
            sealedAt: new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        var earlier = Contract(name: "Starts Earlier", startDate: StartDate, endDate: new DateOnly(2026, 1, 7),
            sealedAt: new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        SetInEffect(later, earlier);

        var result = await CreateHandler().HandleAsync(new GetActiveNenContractQuery(StartDate, new DateOnly(2026, 1, 14)));

        result!.Name.Should().Be("Starts Earlier");
    }

    // ── DayStatuses (per-day floor badges) ──────────────────────────────────────

    [Fact]
    public async Task HandleAsync_DayStatuses_CoversFullContractRange_NotJustPastAndToday()
    {
        // Today is Jan 4; contract runs Jan 1-10 — DayStatuses must include every day,
        // unlike DaysBehind/DaysConsidered which stop at today.
        var contract = Contract(
            dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);

        var result = await CreateHandler().HandleAsync(AnyWeek);

        result!.DayStatuses.Should().HaveCount(10);
        result.DayStatuses.Select(d => d.Date).Should().Contain(EndDate); // Jan 10, well after "today"
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_PastDay_UsesCompletedHours()
    {
        var contract = Contract(
            dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [StartDate] = 2 });

        var result = await CreateHandler().HandleAsync(AnyWeek);

        var day = result!.DayStatuses.Single(d => d.Date == StartDate);
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
            dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [StartDate] = 1 });

        var result = await CreateHandler().HandleAsync(AnyWeek);

        result!.DayStatuses.Single(d => d.Date == StartDate).IsMet.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_FutureDay_UsesPlannedHoursNotCompletedHours()
    {
        // Today is Jan 4; Jan 6 is a future day within the contract.
        var futureDate = StartDate.AddDays(5);
        var contract = Contract(
            dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [futureDate] = 0 });
        _plannedHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [futureDate] = 3 });

        var result = await CreateHandler().HandleAsync(AnyWeek);

        var day = result!.DayStatuses.Single(d => d.Date == futureDate);
        day.CurrentHours.Should().Be(3); // planned, not the 0 completed hours
        day.IsMet.Should().BeTrue();
        day.Timing.Should().Be(DayTiming.Future);
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_Today_IsMarkedTimingToday()
    {
        var contract = Contract(
            dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);

        var result = await CreateHandler().HandleAsync(AnyWeek);

        var day = result!.DayStatuses.Single(d => d.Date == DateOnly.FromDateTime(Today));
        day.Timing.Should().Be(DayTiming.Today);
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_PastDay_IsMarkedTimingPast()
    {
        var contract = Contract(
            dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);

        var result = await CreateHandler().HandleAsync(AnyWeek);

        var day = result!.DayStatuses.Single(d => d.Date == StartDate); // Jan 1, today is Jan 4
        day.Timing.Should().Be(DayTiming.Past);
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_RestDay_IsMetTrueWithZeroFloor()
    {
        var restDay = StartDate.AddDays(1);
        var policy = new DailyFloorPolicy(new DailyFloor(2), new Dictionary<DateOnly, DailyFloor?> { [restDay] = null });
        var contract = Contract(dailyFloorPolicy: policy, plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);

        var result = await CreateHandler().HandleAsync(AnyWeek);

        var day = result!.DayStatuses.Single(d => d.Date == restDay);
        day.IsRestDay.Should().BeTrue();
        day.IsMet.Should().BeTrue();
        day.FloorHours.Should().Be(0);
        day.CurrentHours.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_DayStatuses_ResolvesFloorHoursPerDay()
    {
        var policy = new DailyFloorPolicy(null, new Dictionary<DateOnly, DailyFloor?>
        {
            [StartDate] = new DailyFloor(2),
            [StartDate.AddDays(1)] = new DailyFloor(5)
        });
        var contract = Contract(
            dailyFloorPolicy: policy,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [StartDate] = 2, [StartDate.AddDays(1)] = 5 });
        SetInEffect(contract);

        var result = await CreateHandler().HandleAsync(AnyWeek);

        result!.DayStatuses.Single(d => d.Date == StartDate).FloorHours.Should().Be(2);
        result.DayStatuses.Single(d => d.Date == StartDate.AddDays(1)).FloorHours.Should().Be(5);
    }

    // ── ContractFloor mode (Period Time Budget, no per-day floor) ──────────────────

    [Fact]
    public async Task HandleAsync_OrdinaryContract_ContractFloorHoursIsNull()
    {
        var contract = Contract();
        SetInEffect(contract);

        var result = await CreateHandler().HandleAsync(AnyWeek);

        result!.ContractFloorHours.Should().BeNull();
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

        var result = await CreateHandler().HandleAsync(AnyWeek);

        result!.ContractFloorHours.Should().Be(9);
        result.TotalFloorHours.Should().Be(9);
        result.TotalCompletedHours.Should().Be(4.5);
    }

    [Fact]
    public async Task HandleAsync_OrdinaryContract_TotalFloorHoursSumsEveryDaysFloor()
    {
        var contract = Contract(
            dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);

        var result = await CreateHandler().HandleAsync(AnyWeek);

        result!.TotalFloorHours.Should().Be(20); // 2h * 10 days
    }

    [Fact]
    public async Task HandleAsync_OrdinaryContract_TotalCompletedHoursSumsCompletedAcrossAllDays()
    {
        var contract = Contract(
            dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(2)),
            plannedFocusHoursByDay: FullyPlanned(StartDate, EndDate, 2));
        SetInEffect(contract);
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>
            {
                [StartDate] = 2, [StartDate.AddDays(1)] = 1.5
            });

        var result = await CreateHandler().HandleAsync(AnyWeek);

        result!.TotalCompletedHours.Should().Be(3.5);
    }

    [Fact]
    public async Task HandleAsync_ContractFloorMode_NoDayIsEverRestDay()
    {
        // Nothing planned or completed anywhere — an ordinary contract would resolve
        // every day to a rest day (no floor); ContractFloor mode never does.
        SetInEffect(ContractFloorContract(9));

        var result = await CreateHandler().HandleAsync(AnyWeek);

        result!.DayStatuses.Should().OnlyContain(d => !d.IsRestDay);
    }

    [Fact]
    public async Task HandleAsync_ContractFloorMode_PastDayFullyCompleted_IsMet()
    {
        SetInEffect(ContractFloorContract(9));
        _plannedHours.HandleAsync(Arg.Any<ComputePlannedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [StartDate] = 2 });
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [StartDate] = 2 });

        var result = await CreateHandler().HandleAsync(AnyWeek);

        var day = result!.DayStatuses.Single(d => d.Date == StartDate);
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

        var result = await CreateHandler().HandleAsync(AnyWeek);

        var day = result!.DayStatuses.Single(d => d.Date == StartDate);
        day.CurrentHours.Should().Be(1);
        day.IsMet.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ContractFloorMode_DayWithNothingPlannedOrCompleted_StillMet()
    {
        // A day emptied on purpose (hours moved elsewhere) shows as met, not a failure —
        // 0 completed against 0 planned is trivially satisfied.
        SetInEffect(ContractFloorContract(9));

        var result = await CreateHandler().HandleAsync(AnyWeek);

        var day = result!.DayStatuses.Single(d => d.Date == StartDate);
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

        var result = await CreateHandler().HandleAsync(AnyWeek);

        var day = result!.DayStatuses.Single(d => d.Date == futureDate);
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

        var result = await CreateHandler().HandleAsync(AnyWeek);

        result!.DaysBehind.Should().Be(0);
        result.DaysConsidered.Should().Be(0);
    }
}
