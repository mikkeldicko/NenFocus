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
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IFocusBlockOutcomeClassifier _classifier = Substitute.For<IFocusBlockOutcomeClassifier>();
    private readonly IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _completedHours =
        Substitute.For<IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>>();
    private readonly IQueryHandler<GetNenContractBonusFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _bonusHours =
        Substitute.For<IQueryHandler<GetNenContractBonusFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public GetNenContractRecapQueryHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _sessions.GetByOriginatingBlockIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Session?)null);
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>());
        _bonusHours.HandleAsync(Arg.Any<GetNenContractBonusFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>());
    }

    private GetNenContractRecapQueryHandler CreateHandler() =>
        new(_contracts, _blocks, _sessions, _classifier, _completedHours, _bonusHours, _clock);

    private static NenContract CompletedContract(double committedHours = 4, StrictnessRule strictness = StrictnessRule.Locked) =>
        NenContract.Seal(
            id: ContractId, name: "Focused Week", goal: new NenContractGoal("Ship it"),
            startDate: StartDate, endDate: EndDate, strictnessRule: strictness,
            dailyFloorPolicy: DailyFloorPolicy.None, changeWindow: null, allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [StartDate] = committedHours },
            sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

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

        _classifier.Classify(completedOnTime.Start, completedOnTime.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now)
            .Returns(FocusBlockOutcome.CompletedOnTime);
        _classifier.Classify(startedLate.Start, startedLate.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now)
            .Returns(FocusBlockOutcome.StartedLate);
        _classifier.Classify(endedEarly.Start, endedEarly.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now)
            .Returns(FocusBlockOutcome.EndedEarly);
        _classifier.Classify(missed.Start, missed.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now)
            .Returns(FocusBlockOutcome.Missed);
        _classifier.Classify(redemption.Start, redemption.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Now)
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
        _classifier.DidNotReceiveWithAnyArgs().Classify(default, default, default, default, default);
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
            id: ContractId, name: "Empty", goal: new NenContractGoal("Ship it"),
            startDate: StartDate, endDate: EndDate, strictnessRule: StrictnessRule.Locked,
            dailyFloorPolicy: DailyFloorPolicy.None, changeWindow: null, allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[]);

        var result = await CreateHandler().HandleAsync(new GetNenContractRecapQuery(ContractId), CancellationToken.None);

        result!.CompletionGrade.Should().BeNull();
        result.CompletionPercentage.Should().BeNull();
    }
}
