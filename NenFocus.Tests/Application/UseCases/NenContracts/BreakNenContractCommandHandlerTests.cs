using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractRecap;
using NenFocus.Application.UseCases.NenContracts.Break;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class BreakNenContractCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly ISystemClock _clock = TestClock.Create();
    private readonly IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?> _recap =
        Substitute.For<IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?>>();

    public BreakNenContractCommandHandlerTests()
    {
        _clock.SetNow(Now);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
        _recap.HandleAsync(Arg.Any<GetNenContractRecapQuery>(), Arg.Any<CancellationToken>())
            .Returns(new NenContractRecapDto());
    }

    private BreakNenContractCommandHandler CreateHandler() =>
        new(_contracts, _blocks, _sessions, _clock, _recap);

    private static Session CreatePlannedSession(Guid originatingBlockId)
    {
        var session = Session.Create(
            Guid.NewGuid(),
            new SessionGoal("Focus"),
            new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(),
            SessionOrigin.Planned,
            originatingBlockId: originatingBlockId);
        session.Start(At.Utc(Now.AddMinutes(-10)));
        return session;
    }

    private static NenContract ActiveCancellableContract() => TestContracts.Seal(
        id: Guid.NewGuid(),
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.DailyFlexibility,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    [Fact]
    public async Task HandleAsync_ContractNotFound_FailsWithoutMutating()
    {
        _contracts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(
            new BreakNenContractCommand { ContractId = Guid.NewGuid() }, CancellationToken.None);

        result.Success.Should().BeFalse();
        await _contracts.DidNotReceive().SaveAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidContract_BreaksItAndSaves()
    {
        var contract = ActiveCancellableContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);

        var result = await CreateHandler().HandleAsync(
            new BreakNenContractCommand { ContractId = contract.Id }, CancellationToken.None);

        result.Success.Should().BeTrue();
        contract.Status.Should().Be(NenContractStatus.Broken);
        await _contracts.Received(1).SaveAsync(contract, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_CancelsOnlyBlocksThatHaveNotEnded()
    {
        var contract = ActiveCancellableContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var pastBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-3), Now.AddHours(-1)), membership: TestContracts.Committed(contract.Id)); // already ended
        var currentBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddHours(-1), Now.AddHours(1)), membership: TestContracts.Committed(contract.Id)); // active right now
        var futureBlock = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddDays(1), Now.AddDays(1).AddHours(1)), membership: TestContracts.Committed(contract.Id)); // upcoming

        // pastBlock: never ritual-completed and its scheduled End has already passed — Missed.

        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[pastBlock, currentBlock, futureBlock]);

        await CreateHandler().HandleAsync(new BreakNenContractCommand { ContractId = contract.Id }, CancellationToken.None);

        pastBlock.CancelledAt.Should().BeNull();
        currentBlock.CancelledAt.Should().Be(At.Utc(Now));
        futureBlock.CancelledAt.Should().Be(At.Utc(Now));
        await _blocks.DidNotReceive().SaveAsync(pastBlock, Arg.Any<CancellationToken>());
        await _blocks.Received(1).SaveAsync(currentBlock, Arg.Any<CancellationToken>());
        await _blocks.Received(1).SaveAsync(futureBlock, Arg.Any<CancellationToken>());
        await _blocks.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_LinkedBlockEndedSessionEarly_LeftUncancelled()
    {
        // A FocusBlock whose session already ended early (raw End still in the future) must
        // not be retroactively relabeled Cancelled just because breaking the contract ran
        // before its original scheduled End arrived — it already has a resolved outcome.
        var contract = ActiveCancellableContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var endedEarly = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(Now.AddMinutes(-30), Now.AddHours(1)), membership: TestContracts.Committed(contract.Id));
        endedEarly.MarkRitualCompleted(At.Utc(Now.AddMinutes(-30)));
        endedEarly.MarkSessionEnded(At.Utc(Now.AddMinutes(-5)));

        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[endedEarly]);

        await CreateHandler().HandleAsync(new BreakNenContractCommand { ContractId = contract.Id }, CancellationToken.None);

        endedEarly.CancelledAt.Should().BeNull();
        await _blocks.DidNotReceive().SaveAsync(endedEarly, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BonusBlocks_NotStartedIsReleased_StartedIsLeftWithItsMembership()
    {
        // Gap 97: the handler passes "keep" until Gap 108 adds the prompt — today's behaviour.
        var contract = ActiveCancellableContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var upcomingBonus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddHours(2), Now.AddHours(3)), membership: TestContracts.Bonus(contract.Id));
        var runningBonus = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddHours(-1), Now.AddHours(1)), membership: TestContracts.Bonus(contract.Id));
        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[upcomingBonus, runningBonus]);

        await CreateHandler().HandleAsync(new BreakNenContractCommand { ContractId = contract.Id }, CancellationToken.None);

        upcomingBonus.Membership.Should().BeNull();
        upcomingBonus.CancelledAt.Should().BeNull();
        await _blocks.Received(1).SaveAsync(upcomingBonus, Arg.Any<CancellationToken>());

        runningBonus.Membership.Should().Be(TestContracts.Bonus(contract.Id));
        runningBonus.CancelledAt.Should().BeNull();
        await _blocks.DidNotReceive().SaveAsync(runningBonus, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NoLinkedBlocks_CancelsNothing()
    {
        var contract = ActiveCancellableContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);

        await CreateHandler().HandleAsync(new BreakNenContractCommand { ContractId = contract.Id }, CancellationToken.None);

        await _blocks.DidNotReceive().SaveAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]  // the session of the contract's own running block
    [InlineData(false)] // any other session
    public async Task HandleAsync_WhileASessionRuns_IsRefused_AndNothingChanges(bool sessionOfTheContractsBlock)
    {
        // Gap 95: no contract ends while Focus time is being worked — the handler refuses on its
        // own, even if a caller skipped the validator.
        var contract = ActiveCancellableContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var currentBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddHours(-1), Now.AddHours(1)), membership: TestContracts.Committed(contract.Id));
        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[currentBlock]);
        var session = CreatePlannedSession(sessionOfTheContractsBlock ? currentBlock.Id : Guid.NewGuid());
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateHandler().HandleAsync(new BreakNenContractCommand { ContractId = contract.Id }, CancellationToken.None);

        result.Success.Should().BeFalse();
        contract.Status.Should().Be(NenContractStatus.Active);
        currentBlock.CancelledAt.Should().BeNull();
        session.Status.Should().Be(SessionStatus.Active);
        await _contracts.DidNotReceive().SaveAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
        await _blocks.DidNotReceive().SaveAsync(Arg.Any<PlannedBlock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidContract_ReturnsRecapForImmediatePostContractOverlay()
    {
        var contract = ActiveCancellableContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        var recap = new NenContractRecapDto { ContractId = contract.Id };
        _recap.HandleAsync(Arg.Any<GetNenContractRecapQuery>(), Arg.Any<CancellationToken>()).Returns(recap);

        var result = await CreateHandler().HandleAsync(
            new BreakNenContractCommand { ContractId = contract.Id }, CancellationToken.None);

        result.Recap.Should().Be(recap);
    }

    [Fact]
    public async Task HandleAsync_NoActiveSession_CompletesWithoutError()
    {
        var contract = ActiveCancellableContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);

        var result = await CreateHandler().HandleAsync(
            new BreakNenContractCommand { ContractId = contract.Id }, CancellationToken.None);

        result.Success.Should().BeTrue();
    }
}
