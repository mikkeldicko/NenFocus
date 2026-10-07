using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractRecap;
using NenFocus.Application.UseCases.NenContracts.CompleteNenContract;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class CompleteNenContractCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 10);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly ISystemClock _clock = TestClock.Create();
    private readonly IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?> _recap =
        Substitute.For<IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?>>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();

    public CompleteNenContractCommandHandlerTests()
    {
        _clock.SetNow(Now);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _blocks.GetForContractAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[]);
        _sessions.GetHistoryAsync(Arg.Any<TimeSlot>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Session>)[]);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
    }

    private CompleteNenContractCommandHandler CreateHandler() => new(_contracts, _blocks, _sessions, _clock, _recap);

    private static NenContract ActiveContract() => TestContracts.Seal(
        id: Guid.NewGuid(),
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    [Fact]
    public async Task HandleAsync_ContractNotFound_Fails()
    {
        _contracts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(new CompleteNenContractCommand { ContractId = Guid.NewGuid() });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_NotReady_IsRefused_AndNothingChanges()
    {
        // Gap 95: the handler refuses on its own (a session running, hours owed, a block not
        // done), even if a caller skipped the validator — the contract's own CheckComplete (Gap 92, D).
        var contract = ActiveContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var running = Session.Create(Guid.NewGuid(), new SessionGoal("Focus"), new SessionDuration(TimeSpan.FromHours(1)), Guid.NewGuid(), SessionOrigin.Spontaneous);
        running.Start(At.Utc(Now));
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(running);

        var result = await CreateHandler().HandleAsync(new CompleteNenContractCommand { ContractId = contract.Id });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be(ContractActionVerdictDto.From(ContractActionVerdict.Refused(ContractActionOutcome.SessionIsRunning)).Message);
        contract.Status.Should().Be(NenContractStatus.Active);
        await _contracts.DidNotReceive().SaveAsync(Arg.Any<NenContract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidContract_CompletesAndSaves()
    {
        var contract = ActiveContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var recap = new NenContractRecapDto { ContractId = contract.Id };
        _recap.HandleAsync(Arg.Any<GetNenContractRecapQuery>(), Arg.Any<CancellationToken>()).Returns(recap);

        var result = await CreateHandler().HandleAsync(new CompleteNenContractCommand { ContractId = contract.Id });

        result.Success.Should().BeTrue();
        contract.Status.Should().Be(NenContractStatus.Completed);
        await _contracts.Received(1).SaveAsync(contract, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidContract_ReturnsRecapForImmediatePostContractOverlay()
    {
        var contract = ActiveContract();
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var recap = new NenContractRecapDto { ContractId = contract.Id, CompletionGrade = "Completed" };
        _recap.HandleAsync(Arg.Any<GetNenContractRecapQuery>(), Arg.Any<CancellationToken>()).Returns(recap);

        var result = await CreateHandler().HandleAsync(new CompleteNenContractCommand { ContractId = contract.Id });

        result.Recap.Should().Be(recap);
    }
}
