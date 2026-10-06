using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckNenContractCancellable;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

/// <summary>
/// Whether the Planner offers Cancel for a contract — NenContract.CheckBreak's answer, the same
/// one the Break validator and handler ask, so the button and the command can't disagree.
/// </summary>
public class CheckNenContractCancellableQueryHandlerTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();

    public CheckNenContractCancellableQueryHandlerTests()
    {
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
    }

    private CheckNenContractCancellableQueryHandler CreateHandler() => new(_contracts, _sessions);

    private NenContract ContractInEffect(bool allowCancellation)
    {
        var contract = TestContracts.Seal(
            id: Guid.NewGuid(), name: "Focused Week", goal: new NenContractGoal("Ship it"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(StartDate, StartDate.AddDays(6), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(), changeWindow: null, allowCancellation: allowCancellation,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(StartDate.ToDateTime(TimeOnly.MinValue)), timeZone: At.UtcZone);
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        return contract;
    }

    private Task<NenFocus.Application.DTOs.ContractEndVerdictDto?> Ask(Guid contractId) =>
        CreateHandler().HandleAsync(new CheckNenContractCancellableQuery(contractId));

    [Fact]
    public async Task HandleAsync_CancellationOn_NoSessionRunning_IsAllowed()
    {
        var contract = ContractInEffect(allowCancellation: true);

        (await Ask(contract.Id))!.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_SealedWithCancellationOff_IsNotAllowed_WithItsReason()
    {
        var contract = ContractInEffect(allowCancellation: false);

        var answer = await Ask(contract.Id);

        answer!.Outcome.Should().Be(ContractEndOutcome.CancellationNotAllowed);
        answer.IsAllowed.Should().BeFalse();
        answer.Message.Should().Contain("sealed without cancellation");
    }

    [Fact]
    public async Task HandleAsync_WhileASessionRuns_IsNotAllowed()
    {
        var contract = ContractInEffect(allowCancellation: true);
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(StartDate.ToDateTime(new TimeOnly(9, 0))));
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);

        (await Ask(contract.Id))!.Outcome.Should().Be(ContractEndOutcome.SessionIsRunning);
    }

    [Fact]
    public async Task HandleAsync_AContractNoLongerInEffect_HasNoAnswer()
    {
        var contract = ContractInEffect(allowCancellation: true);
        contract.Break(At.Utc(StartDate.ToDateTime(new TimeOnly(12, 0))), At.UtcZone);

        (await Ask(contract.Id)).Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_AMissingContract_HasNoAnswer()
    {
        _contracts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        (await Ask(Guid.NewGuid())).Should().BeNull();
    }
}
