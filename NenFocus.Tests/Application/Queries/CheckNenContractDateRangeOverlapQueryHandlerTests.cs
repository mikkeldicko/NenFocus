using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.CheckNenContractDateRangeOverlap;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class CheckNenContractDateRangeOverlapQueryHandlerTests
{
    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();

    private CheckNenContractDateRangeOverlapQueryHandler CreateHandler() => new(_contracts);

    private static NenContract SealedContract(Guid id, string name, DateOnly start, DateOnly end) => NenContract.Seal(
        id: id,
        name: name,
        goal: new NenContractGoal("Goal"),
        startDate: start,
        endDate: end,
        strictnessRule: StrictnessRule.DailyFlexibility,
        dailyFloorPolicy: DailyFloorPolicy.None,
        changeWindow: null,
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    [Fact]
    public async Task HandleAsync_NoInEffectContracts_ReturnsNull()
    {
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[]);

        var result = await CreateHandler().HandleAsync(
            new CheckNenContractDateRangeOverlapQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 7)));

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_OverlappingRange_ReturnsContractName()
    {
        var existing = SealedContract(Guid.NewGuid(), "Study Month", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[existing]);

        var result = await CreateHandler().HandleAsync(
            new CheckNenContractDateRangeOverlapQuery(new DateOnly(2026, 1, 15), new DateOnly(2026, 2, 15)));

        result.Should().Be("Study Month");
    }

    [Fact]
    public async Task HandleAsync_NonOverlappingRange_ReturnsNull()
    {
        var existing = SealedContract(Guid.NewGuid(), "Study Month", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[existing]);

        var result = await CreateHandler().HandleAsync(
            new CheckNenContractDateRangeOverlapQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 7)));

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ExcludeContractId_SkipsThatContractEvenIfOverlapping()
    {
        var contractId = Guid.NewGuid();
        var existing = SealedContract(contractId, "Study Month", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[existing]);

        var result = await CreateHandler().HandleAsync(
            new CheckNenContractDateRangeOverlapQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), ExcludeContractId: contractId));

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ExcludeContractId_StillCatchesOverlapWithADifferentContract()
    {
        var editingId = Guid.NewGuid();
        var editing = SealedContract(editingId, "Study Month", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        var other = SealedContract(Guid.NewGuid(), "Fitness Week", new DateOnly(2026, 1, 20), new DateOnly(2026, 1, 27));
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[editing, other]);

        var result = await CreateHandler().HandleAsync(
            new CheckNenContractDateRangeOverlapQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), ExcludeContractId: editingId));

        result.Should().Be("Fitness Week");
    }
}
