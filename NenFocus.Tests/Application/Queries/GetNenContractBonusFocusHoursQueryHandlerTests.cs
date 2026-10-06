using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractBonusFocusHours;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetNenContractBonusFocusHoursQueryHandlerTests
{
    private static readonly Guid ContractId = Guid.NewGuid();
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IPlannedFocusHoursCalculator _calculator = new PlannedFocusHoursCalculator();
    private readonly ISystemClock _clock = TestClock.Create();

    public GetNenContractBonusFocusHoursQueryHandlerTests()
    {
        _clock.SetNow(Now);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
    }

    private GetNenContractBonusFocusHoursQueryHandler CreateHandler() => new(_contracts, _blocks, _calculator, _clock);

    private static NenContract ActiveContract(WorkHours? workHoursPolicy = null) => TestContracts.Seal(
        id: ContractId,
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None(), workHours: workHoursPolicy),
        floorRule: new DailyFloorRule.None(),
        changeWindow: null,
        allowCancellation: false,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        sealedAt: At.Utc(StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);

    [Fact]
    public async Task HandleAsync_ContractNotFound_ReturnsEmpty()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(new GetNenContractBonusFocusHoursQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_BonusBlock_IsCreditedAsBonus()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var day = new DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(day, day.AddHours(1)), membership: TestContracts.Bonus(ContractId));
        block.MarkRitualCompleted(At.Utc(day));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);

        var result = await CreateHandler().HandleAsync(new GetNenContractBonusFocusHoursQuery(ContractId), CancellationToken.None);

        result.GetValueOrDefault(DateOnly.FromDateTime(day)).Should().Be(1.0);
    }

    [Fact]
    public async Task HandleAsync_CommittedBlock_ExcludedFromBonus()
    {
        // Committed/redemption blocks count toward the floor and are counted by
        // GetNenContractCompletedFocusHoursQuery instead — never double-counted here.
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var day = new DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(day, day.AddHours(1)), membership: TestContracts.Committed(ContractId));
        block.MarkRitualCompleted(At.Utc(day));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);

        var result = await CreateHandler().HandleAsync(new GetNenContractBonusFocusHoursQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_BackgroundBlock_Excluded()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var day = new DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc);
        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(),At.Slot(day, day.AddHours(1)), membership: TestContracts.Bonus(ContractId));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);

        var result = await CreateHandler().HandleAsync(new GetNenContractBonusFocusHoursQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
