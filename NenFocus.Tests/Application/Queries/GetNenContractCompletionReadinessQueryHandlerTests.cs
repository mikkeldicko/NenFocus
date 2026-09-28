using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractCompletedFocusHours;
using NenFocus.Application.Queries.GetNenContractCompletionReadiness;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetNenContractCompletionReadinessQueryHandlerTests
{
    private static readonly Guid ContractId = Guid.NewGuid();
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 10); // still days left — completing here ends it early
    private static readonly DateTime Now = new(2026, 1, 3, 12, 0, 0, DateTimeKind.Utc);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>> _completedHours =
        Substitute.For<IQueryHandler<GetNenContractCompletedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();
    private readonly IFocusBlockOutcomeClassifier _outcomeClassifier = Substitute.For<IFocusBlockOutcomeClassifier>();

    public GetNenContractCompletionReadinessQueryHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _clock.ToLocal(Arg.Any<DateTime>()).Returns(ci => ci.Arg<DateTime>());
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
    }

    private GetNenContractCompletionReadinessQueryHandler CreateHandler() =>
        new(_contracts, _blocks, _completedHours, _clock, _outcomeClassifier);

    private static NenContract ActiveContract(double committedHours = 4) => NenContract.Seal(
        id: ContractId,
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        changeWindow: null,
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [StartDate] = committedHours },
        localSealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    private void StubCompleted(double hours) =>
        _completedHours.HandleAsync(Arg.Any<GetNenContractCompletedFocusHoursQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double> { [StartDate] = hours });

    [Fact]
    public async Task HandleAsync_ContractNotFound_NotReady()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletionReadinessQuery(ContractId));

        result.IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ContractNotActive_NotReady()
    {
        var contract = ActiveContract();
        contract.Break(Now);
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletionReadinessQuery(ContractId));

        result.IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_NotAllHoursCompletedYet_NotReady_RemainingHoursReported()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(3);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletionReadinessQuery(ContractId));

        result.IsReady.Should().BeFalse();
        result.RemainingHours.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_AllHoursCompletedNoPendingBlocks_Ready()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(4);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletionReadinessQuery(ContractId));

        result.IsReady.Should().BeTrue();
        result.RemainingHours.Should().Be(0);
        result.PendingBlockCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_OverDelivered_StillReady_RemainingHoursClampedToZero()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(6);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletionReadinessQuery(ContractId));

        result.IsReady.Should().BeTrue();
        result.RemainingHours.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_PendingContractLinkedBlockInFuture_NotReady_PendingCountReported()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(4);
        var pending = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(1), Now.AddHours(2), contractId: ContractId);
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[pending]);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletionReadinessQuery(ContractId));

        result.IsReady.Should().BeFalse();
        result.PendingBlockCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_PendingBonusBlockWithinContractRange_NotReady()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(4);
        var bonusDay = StartDate.AddDays(3).ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
        var pendingBonus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), bonusDay, bonusDay.AddHours(1));
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[pendingBonus]);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletionReadinessQuery(ContractId));

        result.IsReady.Should().BeFalse();
        result.PendingBlockCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_AlreadyEndedContractLinkedBlock_DoesNotCountAsPending()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(4);
        var ended = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-3), Now.AddHours(-2), contractId: ContractId);
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[ended]);
        // Never ritual-completed and its scheduled End has already passed — resolved as Missed.
        _outcomeClassifier.Classify(ended, Now).Returns(FocusBlockOutcome.Missed);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletionReadinessQuery(ContractId));

        result.IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_LinkedBlockEndedSessionEarly_DoesNotCountAsPending()
    {
        // A FocusBlock whose session ended early (raw End still in the future) must not
        // keep the contract "not ready" just because its original scheduled End hasn't
        // arrived — it already has a resolved outcome (EndedEarly).
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(4);
        var endedEarly = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(-30), Now.AddHours(1), contractId: ContractId);
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[endedEarly]);
        _outcomeClassifier.Classify(endedEarly, Now)
            .Returns(FocusBlockOutcome.EndedEarly);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletionReadinessQuery(ContractId));

        result.IsReady.Should().BeTrue();
        result.PendingBlockCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_CancelledPendingBlock_DoesNotCountAsPending()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(4);
        var cancelled = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(1), Now.AddHours(2), contractId: ContractId);
        cancelled.MarkCancelled(Now);
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[cancelled]);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletionReadinessQuery(ContractId));

        result.IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_BonusBlockOutsideContractRange_DoesNotCountAsPending()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(4);
        var outsideDay = EndDate.AddDays(5).ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
        var outsideBonus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), outsideDay, outsideDay.AddHours(1));
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[outsideBonus]);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletionReadinessQuery(ContractId));

        result.IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_BonusBlockAfterWorkHoursCloseOnLastDay_DoesNotCountAsPending()
    {
        // Same calendar day as EndDate but after Work Hours close — EndsAt correctly
        // excludes it, unlike the old date-only truncation this check used to use.
        var policy = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0));
        var contract = NenContract.Seal(
            id: ContractId,
            name: "Focused Week",
            goal: new NenContractGoal("Ship it"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None(), workHours: policy),
            floorRule: new DailyFloorRule.None(),
            changeWindow: null,
            allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [StartDate] = 4 },
            localSealedAt: StartDate.ToDateTime(new TimeOnly(9, 0))); // Active from its 09:00 start
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);
        StubCompleted(4);
        var afterClose = EndDate.ToDateTime(new TimeOnly(18, 0), DateTimeKind.Utc);
        var afterCloseBonus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), afterClose, afterClose.AddHours(1));
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[afterCloseBonus]);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletionReadinessQuery(ContractId));

        result.IsReady.Should().BeTrue();
    }
}
