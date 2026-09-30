using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetInEffectNenContracts;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetInEffectNenContractsQueryHandlerTests
{
    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();

    private GetInEffectNenContractsQueryHandler CreateHandler() => new(_contracts);

    // sealedAt defaults to well before start, so Seal() leaves the contract Scheduled
    // rather than auto-activating it (Status becomes Active only if sealedAt >= startDate).
    private static NenContract SealedContract(
        string name, DateOnly start, DateOnly end, StrictnessRule rule = StrictnessRule.DailyFlexibility) => NenContract.Seal(
            id: Guid.NewGuid(),
            name: name,
            goal: new NenContractGoal("Goal"),
            strictnessRule: rule,
            calendar: TestCalendars.For(start, end, new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            changeWindow: null,
            allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(start.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    private static NenContract ActiveContract(string name, DateOnly start, DateOnly end) => NenContract.Seal(
        id: Guid.NewGuid(),
        name: name,
        goal: new NenContractGoal("Goal"),
        strictnessRule: StrictnessRule.DailyFlexibility,
        calendar: TestCalendars.For(start, end, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        changeWindow: null,
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone); // sealedAt == start -> auto-activates

    [Fact]
    public async Task HandleAsync_NoContracts_ReturnsEmpty()
    {
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[]);

        var result = await CreateHandler().HandleAsync(new GetInEffectNenContractsQuery());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ActiveContract_Included_WithStatus()
    {
        var start = new DateOnly(2026, 3, 1);
        var active = ActiveContract("Currently Running", start, start.AddDays(20));
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[active]);

        var result = await CreateHandler().HandleAsync(new GetInEffectNenContractsQuery());

        var dto = result.Should().ContainSingle().Subject;
        dto.Id.Should().Be(active.Id);
        dto.Status.Should().Be(NenContractStatus.Active);
    }

    [Fact]
    public async Task HandleAsync_ScheduledContract_Included_WithStatus()
    {
        var start = new DateOnly(2026, 6, 1);
        var end = new DateOnly(2026, 6, 30);
        var scheduled = SealedContract("Summer Focus", start, end, StrictnessRule.Locked);
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[scheduled]);

        var result = await CreateHandler().HandleAsync(new GetInEffectNenContractsQuery());

        var dto = result.Should().ContainSingle().Subject;
        dto.Id.Should().Be(scheduled.Id);
        dto.Name.Should().Be("Summer Focus");
        dto.StartDate.Should().Be(start);
        dto.EndDate.Should().Be(end);
        dto.StrictnessRule.Should().Be(StrictnessRule.Locked);
        dto.Status.Should().Be(NenContractStatus.Scheduled);
    }

    [Fact]
    public async Task HandleAsync_ActiveAndScheduled_ActiveOrderedFirst()
    {
        var scheduled = SealedContract("Later Scheduled", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7));
        var active = ActiveContract("Currently Running", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 20));
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[scheduled, active]);

        var result = await CreateHandler().HandleAsync(new GetInEffectNenContractsQuery());

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Currently Running");
        result[1].Name.Should().Be("Later Scheduled");
    }

    [Fact]
    public async Task HandleAsync_MultipleScheduled_OrderedByStartDate()
    {
        var later = SealedContract("Later", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7));
        var earlier = SealedContract("Earlier", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 7));
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[later, earlier]);

        var result = await CreateHandler().HandleAsync(new GetInEffectNenContractsQuery());

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Earlier");
        result[1].Name.Should().Be("Later");
    }
}
