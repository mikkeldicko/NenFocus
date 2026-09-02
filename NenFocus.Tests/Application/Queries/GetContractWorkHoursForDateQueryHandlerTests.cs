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
    private static readonly DateTime Now = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = DateOnly.FromDateTime(Now);
    private static readonly DateOnly EndDate = StartDate.AddDays(30);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();

    private GetContractWorkHoursForDateQueryHandler CreateHandler() => new(_contracts);

    private static NenContract Seal(WorkHoursPolicy? workHoursPolicy, Guid id) =>
        NenContract.Seal(id, "Contract", new NenContractGoal("Goal"), StartDate, EndDate,
            StrictnessRule.DailyFlexibility, new DailyFloorPolicy(DailyFloor.None), null, false,
            new Dictionary<DateOnly, double>(), sealedAt: Now, workHoursPolicy: workHoursPolicy);

    [Fact]
    public async Task Handle_ContractNotFound_ReturnsNull()
    {
        var contractId = Guid.NewGuid();
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(new GetContractWorkHoursForDateQuery(contractId, StartDate));

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ContractHasNoWorkHoursPolicy_ReturnsNull()
    {
        var contractId = Guid.NewGuid();
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(Seal(null, contractId));

        var result = await CreateHandler().HandleAsync(new GetContractWorkHoursForDateQuery(contractId, StartDate));

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_DefaultWorkHours_ReturnsResolvedWindow()
    {
        var contractId = Guid.NewGuid();
        var policy = new WorkHoursPolicy(new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(Seal(policy, contractId));

        var result = await CreateHandler().HandleAsync(new GetContractWorkHoursForDateQuery(contractId, StartDate));

        result.Should().NotBeNull();
        result!.Start.Should().Be(new TimeOnly(9, 0));
        result.End.Should().Be(new TimeOnly(17, 0));
    }

    [Fact]
    public async Task Handle_PerDayExceptionOverridesDefault_ReturnsExceptionWindow()
    {
        var contractId = Guid.NewGuid();
        var exceptionDate = StartDate.AddDays(1);
        var policy = new WorkHoursPolicy(
            new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            new Dictionary<DateOnly, WorkHours?> { [exceptionDate] = new WorkHours(new TimeOnly(11, 0), new TimeOnly(20, 0)) });
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(Seal(policy, contractId));

        var result = await CreateHandler().HandleAsync(new GetContractWorkHoursForDateQuery(contractId, exceptionDate));

        result.Should().NotBeNull();
        result!.Start.Should().Be(new TimeOnly(11, 0));
        result.End.Should().Be(new TimeOnly(20, 0));
    }

    [Fact]
    public async Task Handle_PerDayExceptionExplicitlyNull_ReturnsNoConstraint()
    {
        var contractId = Guid.NewGuid();
        var noConstraintDate = StartDate.AddDays(2);
        var policy = new WorkHoursPolicy(
            new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            new Dictionary<DateOnly, WorkHours?> { [noConstraintDate] = null });
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(Seal(policy, contractId));

        var result = await CreateHandler().HandleAsync(new GetContractWorkHoursForDateQuery(contractId, noConstraintDate));

        result.Should().BeNull();
    }
}
