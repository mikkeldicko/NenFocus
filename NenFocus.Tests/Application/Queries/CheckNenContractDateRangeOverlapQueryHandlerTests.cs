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
        _contracts.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[]);

        var result = await CreateHandler().HandleAsync(
            new CheckNenContractDateRangeOverlapQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 7)));

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_OverlappingRange_ReturnsContractName()
    {
        var existing = SealedContract(Guid.NewGuid(), "Study Month", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        _contracts.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[existing]);

        var result = await CreateHandler().HandleAsync(
            new CheckNenContractDateRangeOverlapQuery(new DateOnly(2026, 1, 15), new DateOnly(2026, 2, 15)));

        result.Should().Be("Study Month");
    }

    [Fact]
    public async Task HandleAsync_NonOverlappingRange_ReturnsNull()
    {
        var existing = SealedContract(Guid.NewGuid(), "Study Month", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        _contracts.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[existing]);

        var result = await CreateHandler().HandleAsync(
            new CheckNenContractDateRangeOverlapQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 7)));

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ExcludeContractId_SkipsThatContractEvenIfOverlapping()
    {
        var contractId = Guid.NewGuid();
        var existing = SealedContract(contractId, "Study Month", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        _contracts.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[existing]);

        var result = await CreateHandler().HandleAsync(
            new CheckNenContractDateRangeOverlapQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), ExcludeContractId: contractId));

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ExistingContractAlreadyCompleted_StillBlocksSameDayStart()
    {
        // The gap this closes: completing a contract used to drop it out of the overlap
        // check entirely (GetAllInEffectAsync excludes Completed), letting a new contract
        // start the very same day the old one finished.
        var existing = SealedContract(Guid.NewGuid(), "Study Month", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10));
        existing.Complete(new DateOnly(2026, 1, 11).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        _contracts.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[existing]);

        var result = await CreateHandler().HandleAsync(
            new CheckNenContractDateRangeOverlapQuery(new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 20)));

        result.Should().Be("Study Month");
    }

    [Fact]
    public async Task HandleAsync_ExistingContractBrokenEarly_OnlyBlocksThroughItsShrunkEndDate()
    {
        // Break() shrinks EndDate to the day it was actually broken (see NenContract.Break) —
        // confirms this query respects that shrunk EndDate rather than the original one.
        var existing = SealedContract(Guid.NewGuid(), "Study Month", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        existing.Break(new DateOnly(2026, 1, 5).ToDateTime(new TimeOnly(14, 0), DateTimeKind.Utc)); // EndDate -> Jan 5
        _contracts.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[existing]);

        var blockedOnBreakDay = await CreateHandler().HandleAsync(
            new CheckNenContractDateRangeOverlapQuery(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 15)));
        var allowedAfterBreakDay = await CreateHandler().HandleAsync(
            new CheckNenContractDateRangeOverlapQuery(new DateOnly(2026, 1, 6), new DateOnly(2026, 1, 15)));

        blockedOnBreakDay.Should().Be("Study Month");
        allowedAfterBreakDay.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ExcludeContractId_StillCatchesOverlapWithADifferentContract()
    {
        var editingId = Guid.NewGuid();
        var editing = SealedContract(editingId, "Study Month", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        var other = SealedContract(Guid.NewGuid(), "Fitness Week", new DateOnly(2026, 1, 20), new DateOnly(2026, 1, 27));
        _contracts.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[editing, other]);

        var result = await CreateHandler().HandleAsync(
            new CheckNenContractDateRangeOverlapQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), ExcludeContractId: editingId));

        result.Should().Be("Fitness Week");
    }
}
