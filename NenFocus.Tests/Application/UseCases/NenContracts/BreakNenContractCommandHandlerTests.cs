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
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();
    private readonly IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?> _recap =
        Substitute.For<IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?>>();
    private readonly IFocusBlockOutcomeClassifier _outcomeClassifier = Substitute.For<IFocusBlockOutcomeClassifier>();

    public BreakNenContractCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
        _recap.HandleAsync(Arg.Any<GetNenContractRecapQuery>(), Arg.Any<CancellationToken>())
            .Returns(new NenContractRecapDto());
    }

    private BreakNenContractCommandHandler CreateHandler() =>
        new(_contracts, _blocks, _sessions, _clock, _recap, _outcomeClassifier);

    private static Session CreatePlannedSession(Guid originatingBlockId)
    {
        var session = Session.Create(
            Guid.NewGuid(),
            new SessionGoal("Focus"),
            new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(),
            SessionOrigin.Planned,
            originatingBlockId: originatingBlockId);
        session.Start(Now.AddMinutes(-10));
        return session;
    }

    private static NenContract ActiveCancellableContract() => NenContract.Seal(
        id: Guid.NewGuid(),
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        startDate: StartDate,
        endDate: EndDate,
        strictnessRule: StrictnessRule.DailyFlexibility,
        dailyFloorPolicy: DailyFloorPolicy.None,
        changeWindow: null,
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

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

        var pastBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            Now.AddHours(-3), Now.AddHours(-1), contractId: contract.Id); // already ended
        var currentBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            Now.AddHours(-1), Now.AddHours(1), contractId: contract.Id); // active right now
        var futureBlock = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            Now.AddDays(1), Now.AddDays(1).AddHours(1), contractId: contract.Id); // upcoming

        // Never ritual-completed and its scheduled End has already passed — resolved as Missed.
        _outcomeClassifier.Classify(pastBlock.Start, pastBlock.End, null, null, Now).Returns(FocusBlockOutcome.Missed);

        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[pastBlock, currentBlock, futureBlock]);

        await CreateHandler().HandleAsync(new BreakNenContractCommand { ContractId = contract.Id }, CancellationToken.None);

        pastBlock.CancelledAt.Should().BeNull();
        currentBlock.CancelledAt.Should().Be(Now);
        futureBlock.CancelledAt.Should().Be(Now);
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

        var endedEarly = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            Now.AddMinutes(-30), Now.AddHours(1), contractId: contract.Id);
        endedEarly.MarkRitualCompleted(Now.AddMinutes(-30));
        endedEarly.MarkSessionEnded(Now.AddMinutes(-5));
        _outcomeClassifier.Classify(endedEarly.Start, endedEarly.End, endedEarly.RitualCompletedAt, endedEarly.SessionEndedAt, Now)
            .Returns(FocusBlockOutcome.EndedEarly);

        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[endedEarly]);

        await CreateHandler().HandleAsync(new BreakNenContractCommand { ContractId = contract.Id }, CancellationToken.None);

        endedEarly.CancelledAt.Should().BeNull();
        await _blocks.DidNotReceive().SaveAsync(endedEarly, Arg.Any<CancellationToken>());
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

    [Fact]
    public async Task HandleAsync_ActiveSessionOriginatingFromCancelledBlock_ForceCompletesSession()
    {
        var contract = ActiveCancellableContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var currentBlock = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            Now.AddHours(-1), Now.AddHours(1), contractId: contract.Id);
        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[currentBlock]);

        var session = CreatePlannedSession(currentBlock.Id);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);

        await CreateHandler().HandleAsync(new BreakNenContractCommand { ContractId = contract.Id }, CancellationToken.None);

        session.Status.Should().Be(SessionStatus.Completed);
        session.EndedAt.Should().Be(Now);
    }

    [Fact]
    public async Task HandleAsync_ActiveSessionOnBlockNotUnderThisContract_LeftUntouched()
    {
        var contract = ActiveCancellableContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        _blocks.GetForContractAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);

        var unrelatedBlockId = Guid.NewGuid();
        var session = CreatePlannedSession(unrelatedBlockId);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);

        await CreateHandler().HandleAsync(new BreakNenContractCommand { ContractId = contract.Id }, CancellationToken.None);

        session.Status.Should().Be(SessionStatus.Active);
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
