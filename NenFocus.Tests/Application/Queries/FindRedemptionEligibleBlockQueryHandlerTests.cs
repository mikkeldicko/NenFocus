using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.FindRedemptionEligibleBlock;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class FindRedemptionEligibleBlockQueryHandlerTests
{
    private static readonly Guid ContractId = Guid.NewGuid();
    private static readonly Guid PresetId = Guid.NewGuid();
    private static readonly DateOnly MissedDate = new(2026, 1, 5);
    private static readonly DateTime SealedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> _deficits =
        Substitute.For<IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>>>();
    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();

    private FindRedemptionEligibleBlockQueryHandler CreateHandler() => new(_deficits, _contracts);

    private static NenContract Contract(StrictnessRule rule) => NenContract.Seal(
        ContractId, "Contract", new NenContractGoal("Goal"), MissedDate, MissedDate.AddDays(14),
        rule, new DailyFloorPolicy(DailyFloor.None), null, false,
        new Dictionary<DateOnly, double>(), SealedAt);

    private static FocusBlockDeficitDto Deficit(DateOnly scheduledDate, Guid? presetId = null) => new()
    {
        FocusBlockId = Guid.NewGuid(),
        BlockPresetId = presetId ?? PresetId,
        ScheduledStart = scheduledDate.ToDateTime(new TimeOnly(10, 0)),
        ScheduledEnd = scheduledDate.ToDateTime(new TimeOnly(12, 0)),
        Outcome = FocusBlockOutcome.Missed,
        RemainingDeficitMinutes = 60
    };

    private void SetDeficits(params FocusBlockDeficitDto[] deficits) =>
        _deficits.HandleAsync(Arg.Is<GetNenContractDeficitsQuery>(q => q.ContractId == ContractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)deficits);

    [Fact]
    public async Task HandleAsync_ContractNotFound_ReturnsNull()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(
            new FindRedemptionEligibleBlockQuery(ContractId, PresetId, MissedDate.ToDateTime(TimeOnly.MinValue)), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_NoDeficitsForPreset_ReturnsNull()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(Contract(StrictnessRule.PeriodTimeBudget));
        SetDeficits(Deficit(MissedDate, presetId: Guid.NewGuid())); // different preset

        var result = await CreateHandler().HandleAsync(
            new FindRedemptionEligibleBlockQuery(ContractId, PresetId, MissedDate.ToDateTime(TimeOnly.MinValue)), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_DailyFlexibility_SameDayProposed_ReturnsMatch()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(Contract(StrictnessRule.DailyFlexibility));
        var deficit = Deficit(MissedDate);
        SetDeficits(deficit);

        var result = await CreateHandler().HandleAsync(
            new FindRedemptionEligibleBlockQuery(ContractId, PresetId, MissedDate.ToDateTime(TimeOnly.MinValue)), CancellationToken.None);

        result.Should().NotBeNull();
        result!.FocusBlockId.Should().Be(deficit.FocusBlockId);
    }

    [Fact]
    public async Task HandleAsync_DailyFlexibility_DifferentDayProposed_ReturnsNull()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(Contract(StrictnessRule.DailyFlexibility));
        SetDeficits(Deficit(MissedDate));

        var result = await CreateHandler().HandleAsync(
            new FindRedemptionEligibleBlockQuery(ContractId, PresetId, MissedDate.AddDays(1).ToDateTime(TimeOnly.MinValue)), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_PeriodTimeBudget_LaterDayProposed_ReturnsMatch()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(Contract(StrictnessRule.PeriodTimeBudget));
        var deficit = Deficit(MissedDate);
        SetDeficits(deficit);

        var result = await CreateHandler().HandleAsync(
            new FindRedemptionEligibleBlockQuery(ContractId, PresetId, MissedDate.AddDays(5).ToDateTime(TimeOnly.MinValue)), CancellationToken.None);

        result.Should().NotBeNull();
        result!.FocusBlockId.Should().Be(deficit.FocusBlockId);
    }

    [Fact]
    public async Task HandleAsync_Locked_NeverEligible_ReturnsNull()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(Contract(StrictnessRule.Locked));
        SetDeficits(Deficit(MissedDate));

        var result = await CreateHandler().HandleAsync(
            new FindRedemptionEligibleBlockQuery(ContractId, PresetId, MissedDate.ToDateTime(TimeOnly.MinValue)), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_MultipleEligibleMatches_ReturnsEarliest()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(Contract(StrictnessRule.PeriodTimeBudget));
        var earlier = Deficit(MissedDate);
        var later = Deficit(MissedDate.AddDays(2));
        SetDeficits(later, earlier);

        var result = await CreateHandler().HandleAsync(
            new FindRedemptionEligibleBlockQuery(ContractId, PresetId, MissedDate.AddDays(10).ToDateTime(TimeOnly.MinValue)), CancellationToken.None);

        result.Should().NotBeNull();
        result!.FocusBlockId.Should().Be(earlier.FocusBlockId);
    }
}
