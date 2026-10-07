using FluentAssertions;
using NenFocus.Application.Interfaces;
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
    private readonly List<PlannedBlock> _delivered = [];
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly ISystemClock _clock = TestClock.Create();

    public GetNenContractCompletionReadinessQueryHandlerTests()
    {
        _clock.SetNow(Now);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _sessions.GetHistoryAsync(Arg.Any<TimeSlot>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Session>)[]);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
    }

    private GetNenContractCompletionReadinessQueryHandler CreateHandler() =>
        new(_contracts, _blocks, _sessions, _clock);

    private static NenContract ActiveContract(double committedHours = 4) => TestContracts.Seal(
        id: ContractId,
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [StartDate] = committedHours },
        sealedAt: At.Utc(StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    // Completed hours are the contract's own (DayStatuses, Gap 95): deliver them with one fully
    // worked Committed block on the first day.
    private void StubCompleted(double hours)
    {
        var start = StartDate.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc);
        _delivered.Add(TestBlocks.Worked(
            FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(start, start.AddHours(hours)), membership: TestContracts.Committed(ContractId)),
            ritualAfterStart: 0, endedAfterStart: hours * 60));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)_delivered);
    }

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
        contract.Break(At.Utc(Now), At.UtcZone, sessionRunning: false);
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
        var pending = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(1), Now.AddHours(2)), membership: TestContracts.Committed(ContractId));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[.. _delivered, pending]);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletionReadinessQuery(ContractId));

        result.IsReady.Should().BeFalse();
        result.PendingBlockCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_PendingBonusBlock_NotReady()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(4);
        var bonusDay = StartDate.AddDays(3).ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
        var pendingBonus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(bonusDay, bonusDay.AddHours(1)), membership: TestContracts.Bonus(ContractId));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[.. _delivered, pendingBonus]);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletionReadinessQuery(ContractId));

        result.IsReady.Should().BeFalse();
        result.PendingBlockCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_AlreadyEndedContractLinkedBlock_DoesNotCountAsPending()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(4);
        var ended = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-3), Now.AddHours(-2)), membership: TestContracts.Committed(ContractId));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[.. _delivered, ended]);
        // Never ritual-completed and its scheduled End has already passed — resolved as Missed.

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
        var endedEarly = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddMinutes(-30), Now.AddHours(1)), membership: TestContracts.Committed(ContractId));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[.. _delivered, endedEarly]);
        TestBlocks.Worked(endedEarly, ritualAfterStart: 0, endedAfterStart: 10);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletionReadinessQuery(ContractId));

        result.IsReady.Should().BeTrue();
        result.PendingBlockCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_LinkedBlockWithASessionStillRunning_IsPending()
    {
        // Gap 95 decision 11: a block whose session is still running isn't done — it could still
        // end early, and its hours are still growing. Completing now would freeze the recap
        // mid-session, so the contract isn't ready, even with every committed hour delivered.
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(4);
        var running = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddMinutes(-30), Now.AddHours(1)), membership: TestContracts.Committed(ContractId));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[.. _delivered, running]);
        TestBlocks.Worked(running, ritualAfterStart: 0, endedAfterStart: null);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletionReadinessQuery(ContractId));

        result.IsReady.Should().BeFalse();
        result.PendingBlockCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_ASpontaneousSessionRunning_IsNotReady()
    {
        // Gap 95: no contract ends while Focus time is being worked — any session, not only one of
        // the contract's own blocks.
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(4);
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)), Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(Now.AddMinutes(-10)));
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletionReadinessQuery(ContractId));

        result.IsReady.Should().BeFalse();
        result.NotReadyReason.Should().Contain("session is running");
    }

    [Fact]
    public async Task HandleAsync_CancelledPendingBlock_DoesNotCountAsPending()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract(committedHours: 4));
        StubCompleted(4);
        var cancelled = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(1), Now.AddHours(2)), membership: TestContracts.Committed(ContractId));
        cancelled.MarkCancelled(At.Utc(Now));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[.. _delivered, cancelled]);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletionReadinessQuery(ContractId));

        result.IsReady.Should().BeTrue();
    }

}
