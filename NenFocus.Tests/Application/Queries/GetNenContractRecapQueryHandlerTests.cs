using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractBonusFocusHours;
using NenFocus.Application.Queries.GetNenContractCompletedFocusHours;
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
    private readonly IFocusBlockOutcomeClassifier _classifier = Substitute.For<IFocusBlockOutcomeClassifier>();
    private readonly IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _completedHours =
        Substitute.For<IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>>();
    private readonly IQueryHandler<GetNenContractBonusFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _bonusHours =
        Substitute.For<IQueryHandler<GetNenContractBonusFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public GetNenContractRecapQueryHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _clock.ToLocal(Arg.Any<DateTime>()).Returns(ci => ci.Arg<DateTime>());
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>());
        _bonusHours.HandleAsync(Arg.Any<GetNenContractBonusFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>());
    }

    private GetNenContractRecapQueryHandler CreateHandler() =>
        new(_contracts, _blocks, _classifier, _completedHours, _bonusHours, _clock);

    private static NenContract CompletedContract(double committedHours = 4, StrictnessRule strictness = StrictnessRule.Locked) =>
        NenContract.Seal(
            id: ContractId,
            name: "Focused Week",
            goal: new NenContractGoal("Ship it"),
            strictnessRule: strictness,
            calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            changeWindow: null,
            allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [StartDate] = committedHours },
            localSealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    // StartDate..EndDate is a 7-day range (Jan 1-7). A fixed 4h/day floor everywhere by
    // default; individual tests mark specific days as rest days.
    private static NenContract FloorContract(IReadOnlyCollection<DateOnly>? restDays = null) => NenContract.Seal(
        id: ContractId, name: "Focused Week", goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.Fixed(new DailyFloor(4)), restDays: restDays),
        floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)),
        changeWindow: null, allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>
        {
            [StartDate] = 4, [StartDate.AddDays(1)] = 4, [StartDate.AddDays(2)] = 4, [StartDate.AddDays(3)] = 4,
            [StartDate.AddDays(4)] = 4, [StartDate.AddDays(5)] = 4, [EndDate] = 4
        },
        localSealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    private void StubCompletedByDay(Dictionary<DateOnly, double> hoursByDay) =>
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)hoursByDay);

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
        });

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

        // Distinct Start times — the classifier is stubbed by (Start, End), so identical
        // windows across blocks would collide and only the last stub would ever match.
        var completedOnTime = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-10), Now.AddHours(-9), contractId: ContractId);
        var startedLate = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-8), Now.AddHours(-7), contractId: ContractId);
        var endedEarly = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-6), Now.AddHours(-5), contractId: ContractId);
        var missed = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-4), Now.AddHours(-3), contractId: ContractId);
        var redemption = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-2), Now.AddHours(-1), contractId: ContractId,
            redemptionEntries: [new RedemptionEntry(Guid.NewGuid(), 30)]);
        var background = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now, Now.AddHours(1), contractId: ContractId);

        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[completedOnTime, startedLate, endedEarly, missed, redemption, background]);

        _classifier.Classify(completedOnTime, Now)
            .Returns(FocusBlockOutcome.CompletedOnTime);
        _classifier.Classify(startedLate, Now)
            .Returns(FocusBlockOutcome.StartedLate);
        _classifier.Classify(endedEarly, Now)
            .Returns(FocusBlockOutcome.EndedEarly);
        _classifier.Classify(missed, Now)
            .Returns(FocusBlockOutcome.Missed);
        _classifier.Classify(redemption, Now)
            .Returns(FocusBlockOutcome.CompletedOnTime);

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
    public async Task HandleAsync_CancelledBlock_CountedSeparately_NeverClassified()
    {
        var contract = CompletedContract();
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);

        var cancelled = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now, Now.AddHours(1), contractId: ContractId);
        cancelled.MarkCancelled(Now);
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[cancelled]);

        var result = await CreateHandler().HandleAsync(new GetNenContractRecapQuery(ContractId), CancellationToken.None);

        result!.CancelledBlocks.Should().Be(1);
        result.CompletedOnTimeBlocks.Should().Be(0);
        result.StartedLateBlocks.Should().Be(0);
        result.EndedEarlyBlocks.Should().Be(0);
        result.MissedBlocks.Should().Be(0);
        _classifier.DidNotReceiveWithAnyArgs().Classify(default!, default);
    }

    [Fact]
    public async Task HandleAsync_BonusBlockCount_MatchesBonusResolutionRule()
    {
        var contract = CompletedContract();
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[]);

        var inRangeDay = StartDate.AddDays(2).ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
        var bonusInRange = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), inRangeDay, inRangeDay.AddHours(1)); // no ContractId
        var outsideRangeDay = EndDate.AddDays(5).ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
        var bonusOutsideRange = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), outsideRangeDay, outsideRangeDay.AddHours(1));

        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[bonusInRange, bonusOutsideRange]);

        var result = await CreateHandler().HandleAsync(new GetNenContractRecapQuery(ContractId), CancellationToken.None);

        result!.BonusBlockCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_BonusBlockCount_IgnoresBackgroundBlocks()
    {
        // Bonus hours only ever come from Focus blocks (GetNenContractBonusFocusHoursQueryHandler),
        // so the count of Bonus blocks must come from the same set — an unlinked Background
        // block inside the contract's range earns nothing and is not a Bonus block.
        var contract = CompletedContract();
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[]);

        var inRangeDay = StartDate.AddDays(2).ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
        var focusBonus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), inRangeDay, inRangeDay.AddHours(1));
        var unlinkedBackground = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), inRangeDay.AddHours(2), inRangeDay.AddHours(3));

        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[focusBonus, unlinkedBackground]);

        var result = await CreateHandler().HandleAsync(new GetNenContractRecapQuery(ContractId), CancellationToken.None);

        result!.BonusBlockCount.Should().Be(1);
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
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [StartDate] = completed });

        var result = await CreateHandler().HandleAsync(new GetNenContractRecapQuery(ContractId), CancellationToken.None);

        result!.CompletionGrade.Should().Be(expectedGrade);
        result.CompletionPercentage.Should().Be(completed / committed);
    }

    [Fact]
    public async Task HandleAsync_NothingEverCommitted_GradeAndPercentageAreNull()
    {
        // CommittedFocusHours == 0 (nothing was ever planned at Seal) — can't be graded.
        var contract = NenContract.Seal(
            id: ContractId,
            name: "Empty",
            goal: new NenContractGoal("Ship it"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            changeWindow: null,
            allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            localSealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[]);

        var result = await CreateHandler().HandleAsync(new GetNenContractRecapQuery(ContractId), CancellationToken.None);

        result!.CompletionGrade.Should().BeNull();
        result.CompletionPercentage.Should().BeNull();
    }
}
