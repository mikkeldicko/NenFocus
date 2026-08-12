using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetScheduledNenContracts;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetScheduledNenContractsQueryHandlerTests
{
    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();

    private GetScheduledNenContractsQueryHandler CreateHandler() => new(_contracts);

    // sealedAt defaults to well before start, so Seal() leaves the contract Scheduled
    // rather than auto-activating it (Status becomes Active only if sealedAt >= startDate).
    private static NenContract SealedContract(
        string name, DateOnly start, DateOnly end, StrictnessRule rule = StrictnessRule.DailyFlexibility) => NenContract.Seal(
        id: Guid.NewGuid(),
        name: name,
        goal: new NenContractGoal("Goal"),
        startDate: start,
        endDate: end,
        strictnessRule: rule,
        dailyFloorPolicy: DailyFloorPolicy.None,
        changeWindow: null,
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: start.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    [Fact]
    public async Task HandleAsync_NoContracts_ReturnsEmpty()
    {
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[]);

        var result = await CreateHandler().HandleAsync(new GetScheduledNenContractsQuery());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ActiveContract_Excluded()
    {
        var start = new DateOnly(2026, 3, 1);
        var active = NenContract.Seal(
            id: Guid.NewGuid(), name: "Currently Running", goal: new NenContractGoal("Goal"),
            startDate: start, endDate: start.AddDays(20), strictnessRule: StrictnessRule.DailyFlexibility,
            dailyFloorPolicy: DailyFloorPolicy.None, changeWindow: null, allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); // sealedAt == start -> auto-activates
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[active]);

        var result = await CreateHandler().HandleAsync(new GetScheduledNenContractsQuery());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ScheduledContract_Included()
    {
        var start = new DateOnly(2026, 6, 1);
        var end = new DateOnly(2026, 6, 30);
        var scheduled = SealedContract("Summer Focus", start, end, StrictnessRule.Locked);
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[scheduled]);

        var result = await CreateHandler().HandleAsync(new GetScheduledNenContractsQuery());

        var dto = result.Should().ContainSingle().Subject;
        dto.Id.Should().Be(scheduled.Id);
        dto.Name.Should().Be("Summer Focus");
        dto.StartDate.Should().Be(start);
        dto.EndDate.Should().Be(end);
        dto.StrictnessRule.Should().Be(StrictnessRule.Locked);
    }

    [Fact]
    public async Task HandleAsync_MultipleScheduled_OrderedByStartDate()
    {
        var later = SealedContract("Later", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7));
        var earlier = SealedContract("Earlier", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 7));
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[later, earlier]);

        var result = await CreateHandler().HandleAsync(new GetScheduledNenContractsQuery());

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Earlier");
        result[1].Name.Should().Be("Later");
    }
}
