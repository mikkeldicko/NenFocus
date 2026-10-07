using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractRecap;
using NenFocus.Application.Queries.GetUnacknowledgedPostContracts;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetUnacknowledgedPostContractsQueryHandlerTests
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?> _recap =
        Substitute.For<IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?>>();

    private GetUnacknowledgedPostContractsQueryHandler CreateHandler() => new(_contracts, _recap);

    private static NenContract CompletedContract(Guid id) => TestContracts.Seal(
        id: id,
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    [Fact]
    public async Task HandleAsync_NoUnacknowledgedContracts_ReturnsEmpty()
    {
        _contracts.GetUnacknowledgedPostContractsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[]);

        var result = await CreateHandler().HandleAsync(new GetUnacknowledgedPostContractsQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_BuildsOneRecapPerUnacknowledgedContract()
    {
        var contract1 = CompletedContract(Guid.NewGuid());
        var contract2 = CompletedContract(Guid.NewGuid());
        _contracts.GetUnacknowledgedPostContractsAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<NenContract>)[contract1, contract2]);
        _recap.HandleAsync(new GetNenContractRecapQuery(contract1.Id), Arg.Any<CancellationToken>())
            .Returns(new NenContractRecapDto { ContractId = contract1.Id, Name = "One" });
        _recap.HandleAsync(new GetNenContractRecapQuery(contract2.Id), Arg.Any<CancellationToken>())
            .Returns(new NenContractRecapDto { ContractId = contract2.Id, Name = "Two" });

        var result = await CreateHandler().HandleAsync(new GetUnacknowledgedPostContractsQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().Contain(r => r.Name == "One");
        result.Should().Contain(r => r.Name == "Two");
    }

    [Fact]
    public async Task HandleAsync_RecapReturnsNull_ContractSkipped()
    {
        var contract = CompletedContract(Guid.NewGuid());
        _contracts.GetUnacknowledgedPostContractsAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<NenContract>)[contract]);
        _recap.HandleAsync(new GetNenContractRecapQuery(contract.Id), Arg.Any<CancellationToken>())
            .Returns((NenContractRecapDto?)null);

        var result = await CreateHandler().HandleAsync(new GetUnacknowledgedPostContractsQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
