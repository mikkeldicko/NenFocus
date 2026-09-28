using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetContractWorkHoursForDate;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetContractWorkHoursForDateQueryHandlerTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = StartDate.AddDays(30);
    private static readonly WorkHours NineToFive = new(new TimeOnly(9, 0), new TimeOnly(17, 0));

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();

    private GetContractWorkHoursForDateQueryHandler CreateHandler() => new(_contracts);

    private static NenContract Seal(Guid id, WorkHours? workHours, IReadOnlyCollection<DateOnly>? noWorkHoursDays = null) =>
        NenContract.Seal(
            id: id,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.DailyFlexibility,
            calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None(), workHours: workHours, noWorkHoursDays: noWorkHoursDays),
            floorRule: new DailyFloorRule.None(),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            localSealedAt: StartDate.ToDateTime(TimeOnly.MinValue));

    private static DateTime At(DateOnly date, int hour) => date.ToDateTime(new TimeOnly(hour, 0));

    [Fact]
    public async Task Handle_ContractNotFound_ReturnsNull()
    {
        var contractId = Guid.NewGuid();
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(new GetContractWorkHoursForDateQuery(contractId, StartDate));

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_DateOutsideTheContract_ReturnsNull()
    {
        var contractId = Guid.NewGuid();
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(Seal(contractId, NineToFive));

        var result = await CreateHandler().HandleAsync(new GetContractWorkHoursForDateQuery(contractId, EndDate.AddDays(1)));

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoWorkHours_ReturnsTheWholeDay()
    {
        var contractId = Guid.NewGuid();
        var date = StartDate.AddDays(1);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(Seal(contractId, workHours: null));

        var result = await CreateHandler().HandleAsync(new GetContractWorkHoursForDateQuery(contractId, date));

        result.Should().Be(new NenFocus.Application.DTOs.ContractWorkHoursDto(At(date, 0), At(date.AddDays(1), 0)));
    }

    [Fact]
    public async Task Handle_WorkHours_ReturnsThatDaysWindow()
    {
        var contractId = Guid.NewGuid();
        var date = StartDate.AddDays(1);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(Seal(contractId, NineToFive));

        var result = await CreateHandler().HandleAsync(new GetContractWorkHoursForDateQuery(contractId, date));

        result.Should().Be(new NenFocus.Application.DTOs.ContractWorkHoursDto(At(date, 9), At(date, 17)));
    }

    [Fact]
    public async Task Handle_NoWorkHoursDay_ReturnsTheWholeDay()
    {
        var contractId = Guid.NewGuid();
        var noConstraintDate = StartDate.AddDays(2);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(Seal(contractId, NineToFive, [noConstraintDate]));

        var result = await CreateHandler().HandleAsync(new GetContractWorkHoursForDateQuery(contractId, noConstraintDate));

        result.Should().Be(new NenFocus.Application.DTOs.ContractWorkHoursDto(At(noConstraintDate, 0), At(noConstraintDate.AddDays(1), 0)));
    }
}
