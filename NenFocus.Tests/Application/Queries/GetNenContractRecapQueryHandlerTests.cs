using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractDayStatuses;
using NenFocus.Application.Queries.GetNenContractRecap;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetNenContractRecapQueryHandlerTests
{
    private static readonly Guid ContractId = Guid.NewGuid();
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);
    private static readonly DateTime Now = new(2026, 1, 8, 12, 0, 0, DateTimeKind.Utc); // after EndDate

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IQueryHandler<GetNenContractDayStatusesQuery, IReadOnlyList<ContractDayStatus>> _dayStatuses =
        Substitute.For<IQueryHandler<GetNenContractDayStatusesQuery, IReadOnlyList<ContractDayStatus>>>();
    private readonly ISystemClock _clock = TestClock.Create();

    public GetNenContractRecapQueryHandlerTests()
    {
        _clock.SetNow(Now);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _dayStatuses.HandleAsync(Arg.Any<GetNenContractDayStatusesQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ContractDayStatus>)[]);
    }

    private GetNenContractRecapQueryHandler CreateHandler() =>
        new(_contracts, _blocks, _dayStatuses, _clock);

    private static NenContract CompletedContract(double committedHours = 4, StrictnessRule strictness = StrictnessRule.Locked) =>
        TestContracts.Seal(
            id: ContractId,
            name: "Focused Week",
            goal: new NenContractGoal("Ship it"),
            strictnessRule: strictness,
            calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [StartDate] = committedHours },
            sealedAt: At.Utc(StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    // StartDate..EndDate is a 7-day range (Jan 1-7). A fixed 4h/day floor everywhere by
    // default; individual tests mark specific days as rest days.
    private static NenContract FloorContract(IReadOnlyCollection<DateOnly>? restDays = null) => TestContracts.Seal(
        id: ContractId, name: "Focused Week", goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.Fixed(new DailyFloor(4)), restDays: restDays),
        floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)),
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>
        {
            [StartDate] = 4, [StartDate.AddDays(1)] = 4, [StartDate.AddDays(2)] = 4, [StartDate.AddDays(3)] = 4,
            [StartDate.AddDays(4)] = 4, [StartDate.AddDays(5)] = 4, [EndDate] = 4
        },
        sealedAt: At.Utc(StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    // The recap reads the contract's own days (DayStatuses, Gap 95; its arithmetic is pinned in
    // NenContractDayStatusTests): every day of Jan 1-7 owes 4h, except the rest days given.
    private void StubCompletedByDay(Dictionary<DateOnly, double> hoursByDay, params DateOnly[] restDays)
    {
        var days = Enumerable.Range(0, 7).Select(i => StartDate.AddDays(i)).Select(date =>
        {
            var target = restDays.Contains(date) ? 0 : 4;
            return new ContractDayStatus(date, target, hoursByDay.GetValueOrDefault(date), 0);
        }).ToList();
        _dayStatuses.HandleAsync(Arg.Any<GetNenContractDayStatusesQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ContractDayStatus>)days);
    }

    [Fact]
    public async Task HandleAsync_DailyBreakdown_OneEntryPerDayWithCorrectPercentage()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(FloorContract());
        StubCompletedByDay(new Dictionary<DateOnly, double> { [StartDate] = 2, [StartDate.AddDays(1)] = 4 });

        var result = await CreateHandler().HandleAsync(new GetNenContractRecapQuery(ContractId), CancellationToken.None);

        result!.DailyBreakdown.Should().HaveCount(7); // Jan 1 through Jan 7 inclusive
        result.DailyBreakdown.First(d => d.Date == StartDate).Percentage.Should().Be(0.5);
        result.DailyBreakdown.First(d => d.Date == StartDate.AddDays(1)).Percentage.Should().Be(1.0);
        result.DailyBreakdown.First(d => d.Date == StartDate.AddDays(2)).Percentage.Should().Be(0.0); // no hours logged
    }

    [Fact]
    public async Task HandleAsync_DailyBreakdown_RestDayHasNullPercentageAndIsFlagged()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(FloorContract(restDays: [StartDate.AddDays(3)]));
        StubCompletedByDay(new Dictionary<DateOnly, double>(), StartDate.AddDays(3));

        var result = await CreateHandler().HandleAsync(new GetNenContractRecapQuery(ContractId), CancellationToken.None);

        var restDay = result!.DailyBreakdown.First(d => d.Date == StartDate.AddDays(3));
        restDay.IsRestDay.Should().BeTrue();
        restDay.Percentage.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_BestStreakDays_ConsecutiveMetDaysCounted()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(FloorContract());
        // Met: Jan1, Jan2, Jan3 -> streak of 3. Jan4 not met breaks it. Jan5, Jan6 met -> streak of 2.
        StubCompletedByDay(new Dictionary<DateOnly, double>
        {
            [StartDate] = 4, [StartDate.AddDays(1)] = 4, [StartDate.AddDays(2)] = 4,
            [StartDate.AddDays(3)] = 0,
            [StartDate.AddDays(4)] = 4, [StartDate.AddDays(5)] = 4,
            [EndDate] = 0
        });

        var result = await CreateHandler().HandleAsync(new GetNenContractRecapQuery(ContractId), CancellationToken.None);

        result!.BestStreakDays.Should().Be(3);
    }

    [Fact]
    public async Task HandleAsync_BestStreakDays_RestDayInTheMiddleDoesNotBreakStreak()
    {
        // rest day, sandwiched between two met days
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(FloorContract(restDays: [StartDate.AddDays(1)]));
        StubCompletedByDay(new Dictionary<DateOnly, double>
        {
            [StartDate] = 4, [StartDate.AddDays(2)] = 4
        }, StartDate.AddDays(1));

        var result = await CreateHandler().HandleAsync(new GetNenContractRecapQuery(ContractId), CancellationToken.None);

        result!.BestStreakDays.Should().Be(2); // StartDate + StartDate.AddDays(2), rest day skipped
    }

    [Fact]
    public async Task HandleAsync_BestStreakDays_NoMetDays_IsZero()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(FloorContract());
        StubCompletedByDay(new Dictionary<DateOnly, double>());

        var result = await CreateHandler().HandleAsync(new GetNenContractRecapQuery(ContractId), CancellationToken.None);

        result!.BestStreakDays.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ContractNotFound_ReturnsNull()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(new GetNenContractRecapQuery(ContractId), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_CountsBlocksByOutcome()
    {
        var contract = CompletedContract();
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);

        var completedOnTime = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-10), Now.AddHours(-9)), membership: TestContracts.Committed(ContractId));
        var startedLate = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-8), Now.AddHours(-7)), membership: TestContracts.Committed(ContractId));
        var endedEarly = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-6), Now.AddHours(-5)), membership: TestContracts.Committed(ContractId));
        var missed = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-4), Now.AddHours(-3)), membership: TestContracts.Committed(ContractId));
        var redemption = FocusBlock.CreateWithReceipt(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-2), Now.AddHours(-1)), title: null, membership: TestContracts.Redemption(ContractId),
            receipt: new RedemptionReceipt([new RedemptionEntry(Guid.NewGuid(), WholeMinutes.Of(30))]));
        var background = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now, Now.AddHours(1)), membership: TestContracts.Committed(ContractId));

        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[completedOnTime, startedLate, endedEarly, missed, redemption, background]);

        TestBlocks.Worked(completedOnTime, ritualAfterStart: 0, endedAfterStart: 60);
        TestBlocks.Worked(startedLate, ritualAfterStart: 20, endedAfterStart: 60);
        TestBlocks.Worked(endedEarly, ritualAfterStart: 0, endedAfterStart: 30);
        TestBlocks.Worked(redemption, ritualAfterStart: 0, endedAfterStart: 60);
        // missed: never stamped

        var result = await CreateHandler().HandleAsync(new GetNenContractRecapQuery(ContractId), CancellationToken.None);

        result!.TotalFocusBlocks.Should().Be(5);
        result.TotalBackgroundBlocks.Should().Be(1);
        result.CompletedOnTimeBlocks.Should().Be(2); // completedOnTime + redemption
        result.StartedLateBlocks.Should().Be(1);
        result.EndedEarlyBlocks.Should().Be(1);
        result.MissedBlocks.Should().Be(1);
        result.RedemptionBlocks.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_CancelledBlock_CountedSeparately()
    {
        var contract = CompletedContract();
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);

        var cancelled = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now, Now.AddHours(1)), membership: TestContracts.Committed(ContractId));
        cancelled.MarkCancelled(At.Utc(Now));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[cancelled]);

        var result = await CreateHandler().HandleAsync(new GetNenContractRecapQuery(ContractId), CancellationToken.None);

        result!.CancelledBlocks.Should().Be(1);
        result.CompletedOnTimeBlocks.Should().Be(0);
        result.StartedLateBlocks.Should().Be(0);
        result.EndedEarlyBlocks.Should().Be(0);
        result.MissedBlocks.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_BonusBlocks_AreCountedApart_AndChangeNoOtherNumber()
    {
        // Gap 97: Bonus blocks carry the contract's id now, so they come back with its members —
        // and must stay out of every total and outcome. Only Focus ones are Bonus blocks; a
        // removed (cancelled) one never happened.
        var contract = CompletedContract();
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);

        var committed = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddHours(-10), Now.AddHours(-9)), membership: TestContracts.Committed(ContractId));
        var bonus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddHours(-8), Now.AddHours(-7)), membership: TestContracts.Bonus(ContractId));
        var removedBonus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddHours(2), Now.AddHours(3)), membership: TestContracts.Bonus(ContractId));
        removedBonus.MarkCancelled(At.Utc(Now));
        var bonusBackground = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddHours(-6), Now.AddHours(-5)), membership: TestContracts.Bonus(ContractId));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[committed, bonus, removedBonus, bonusBackground]);
        TestBlocks.Worked(committed, ritualAfterStart: 0, endedAfterStart: 60);

        var result = await CreateHandler().HandleAsync(new GetNenContractRecapQuery(ContractId), CancellationToken.None);

        result!.BonusBlockCount.Should().Be(1);
        result.TotalFocusBlocks.Should().Be(1);
        result.TotalBackgroundBlocks.Should().Be(0);
        result.CompletedOnTimeBlocks.Should().Be(1);
        result.CancelledBlocks.Should().Be(0);
    }

    [Theory]
    [InlineData(4, 4, "Completed")]
    [InlineData(4, 8, "Completed")] // over-delivered still reads Completed
    [InlineData(4, 2, "Partially Completed")]
    [InlineData(4, 0, "Failed")]
    public async Task HandleAsync_CompletionGrade_MatchesBands(double committed, double completed, string expectedGrade)
    {
        var contract = CompletedContract(committedHours: committed);
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[]);
        StubCompletedByDay(new Dictionary<DateOnly, double> { [StartDate] = completed });

        var result = await CreateHandler().HandleAsync(new GetNenContractRecapQuery(ContractId), CancellationToken.None);

        result!.CompletionGrade.Should().Be(expectedGrade);
        result.CompletionPercentage.Should().Be(completed / committed);
    }

    [Fact]
    public async Task HandleAsync_NothingEverCommitted_GradeAndPercentageAreNull()
    {
        // CommittedFocusHours == 0 (nothing was ever planned at Seal) — can't be graded.
        var contract = TestContracts.Seal(
            id: ContractId,
            name: "Empty",
            goal: new NenContractGoal("Ship it"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[]);

        var result = await CreateHandler().HandleAsync(new GetNenContractRecapQuery(ContractId), CancellationToken.None);

        result!.CompletionGrade.Should().BeNull();
        result.CompletionPercentage.Should().BeNull();
    }
}
