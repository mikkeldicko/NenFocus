using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.Break;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.NenContracts;

public class BreakNenContractValidatorTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();

    public BreakNenContractValidatorTests()
    {
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
    }

    private BreakNenContractValidator CreateValidator() => new(_contracts, _sessions);

    private static NenContract Seal(bool allowCancellation, DateTime sealedAt) => TestContracts.Seal(
        id: Guid.NewGuid(),
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.DailyFlexibility,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        allowCancellation: allowCancellation,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(sealedAt), timeZone: At.UtcZone);

    [Fact]
    public async Task Validate_EmptyContractId_IsInvalid()
    {
        var result = await CreateValidator().ValidateAsync(new BreakNenContractCommand { ContractId = Guid.Empty });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ContractNotFound_IsInvalid()
    {
        var id = Guid.NewGuid();
        _contracts.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateValidator().ValidateAsync(new BreakNenContractCommand { ContractId = id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ScheduledAndCancellable_IsValid()
    {
        // Not yet started, but still in effect — cancellable before Day 1.
        var contract = Seal(allowCancellation: true, sealedAt: StartDate.AddDays(-5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); // Scheduled
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(new BreakNenContractCommand { ContractId = contract.Id });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ContractAlreadyCompleted_IsInvalid()
    {
        var contract = Seal(allowCancellation: true, sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); // Active
        contract.Complete(At.Utc(StartDate.AddDays(10).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(new BreakNenContractCommand { ContractId = contract.Id });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_CancellationNotAllowed_IsInvalidWithMessage()
    {
        // The contract's own setting is the gate (decided 2026-10-06, ending the 2026-08-18 interim).
        var contract = Seal(allowCancellation: false, sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); // Active
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(new BreakNenContractCommand { ContractId = contract.Id });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage.Contains("sealed without cancellation"));
    }

    [Fact]
    public async Task Validate_WhileASessionRuns_IsInvalidWithMessage()
    {
        // Gap 95: no contract ends while Focus time is being worked.
        var contract = Seal(allowCancellation: true, sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); // Active
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)), Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(StartDate.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc)));
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateValidator().ValidateAsync(new BreakNenContractCommand { ContractId = contract.Id });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage.Contains("session is running"));
    }

    [Fact]
    public async Task Validate_ActiveAndCancellable_IsValid()
    {
        var contract = Seal(allowCancellation: true, sealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); // Active
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateValidator().ValidateAsync(new BreakNenContractCommand { ContractId = contract.Id });

        result.IsValid.Should().BeTrue();
    }
}
